using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using API.Models;

namespace API.Services.Xray;

/// <summary>
/// Turns a profile's config.json into the config Xray runs: adds what the management app relies on
/// (stats, the gRPC API inbound and its routing rule, per-user stats policy) and fills in the VLESS clients.
/// </summary>
public static class XrayRuntimeConfigBuilder
{
    public const string VisionFlow = "xtls-rprx-vision";
    public const string ApiTag = "api";

    public static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static JsonObject Build(JsonObject profileConfig, IEnumerable<User> enabledUsers, int apiPort)
    {
        var config = (JsonObject)profileConfig.DeepClone();
        EnsureManagementSections(config, apiPort);
        InjectClients(config, enabledUsers);
        return config;
    }

    /// <summary>Replaces the clients of every VLESS inbound (or only <paramref name="onlyTag"/>).</summary>
    public static void InjectClients(JsonObject config, IEnumerable<User> enabledUsers, string? onlyTag = null)
    {
        var users = enabledUsers.ToList();
        foreach (var inbound in VlessInbounds(config))
        {
            if (onlyTag is not null && TagOf(inbound) != onlyTag) continue;
            var flow = DeriveFlow(inbound);
            var settings = GetOrAddObject(inbound, "settings");
            // The email is the secret, the same as the gRPC calls use, so stats and removals match.
            settings["clients"] = new JsonArray(users.Select(u => (JsonNode)Client(u.Secret, flow)).ToArray());
            settings["decryption"] ??= "none";
        }
    }

    /// <summary>Hash of the config without its clients: it only changes when Xray needs a restart.</summary>
    public static string StructureHash(JsonObject runtimeConfig, string profileName)
    {
        var copy = (JsonObject)runtimeConfig.DeepClone();
        foreach (var inbound in VlessInbounds(copy))
            GetOrAddObject(inbound, "settings")["clients"] = new JsonArray();
        var bytes = Encoding.UTF8.GetBytes(profileName + "\n" + copy.ToJsonString());
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static string Serialize(JsonObject config) => config.ToJsonString(Indented);

    public static IReadOnlyList<XrayInboundInfo> DescribeVlessInbounds(JsonObject config) =>
        VlessInbounds(config)
            .Select(inbound => new XrayInboundInfo(
                TagOf(inbound) ?? string.Empty,
                NetworkOf(inbound),
                SecurityOf(inbound),
                PortOf(inbound),
                DeriveFlow(inbound)))
            .ToList();

    /// <summary>
    /// XTLS Vision only works on raw TCP with TLS or REALITY; Xray rejects it on WebSocket and other transports.
    /// </summary>
    public static string DeriveFlow(JsonObject inbound) =>
        NetworkOf(inbound) is "tcp" or "raw" && SecurityOf(inbound) is "tls" or "reality"
            ? VisionFlow
            : string.Empty;

    public static IEnumerable<JsonObject> VlessInbounds(JsonObject config) =>
        (config["inbounds"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(i => string.Equals(AsString(i["protocol"]), "vless", StringComparison.OrdinalIgnoreCase)
                        && TagOf(i) != ApiTag);

    public static string? TagOf(JsonObject inbound) => AsString(inbound["tag"]);

    public static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static string NetworkOf(JsonObject inbound) =>
        (AsString((inbound["streamSettings"] as JsonObject)?["network"]) ?? "tcp").ToLowerInvariant();

    private static string SecurityOf(JsonObject inbound) =>
        (AsString((inbound["streamSettings"] as JsonObject)?["security"]) ?? "none").ToLowerInvariant();

    private static string? PortOf(JsonObject inbound) => inbound["port"] switch
    {
        null => null,
        JsonValue value when value.TryGetValue(out string? text) => text,
        var node => node.ToJsonString(),
    };

    private static JsonObject Client(string secret, string flow)
    {
        var client = new JsonObject { ["id"] = secret, ["email"] = secret };
        if (flow.Length > 0) client["flow"] = flow;
        return client;
    }

    private static void EnsureManagementSections(JsonObject config, int apiPort)
    {
        if (config["stats"] is not JsonObject) config["stats"] = new JsonObject();

        var api = GetOrAddObject(config, "api");
        api["tag"] = ApiTag;
        var services = GetOrAddArray(api, "services");
        foreach (var service in new[] { "HandlerService", "StatsService" })
        {
            if (!services.Any(s => AsString(s) == service)) services.Add(service);
        }

        var inbounds = GetOrAddArray(config, "inbounds");
        if (!inbounds.OfType<JsonObject>().Any(i => TagOf(i) == ApiTag))
        {
            inbounds.Add(new JsonObject
            {
                ["tag"] = ApiTag,
                ["listen"] = "0.0.0.0",
                ["port"] = apiPort,
                ["protocol"] = "dokodemo-door",
                ["settings"] = new JsonObject { ["address"] = "127.0.0.1" },
            });
        }

        var rules = GetOrAddArray(GetOrAddObject(config, "routing"), "rules");
        if (!rules.OfType<JsonObject>().Any(IsApiRule))
        {
            rules.Insert(0, new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = new JsonArray(ApiTag),
                ["outboundTag"] = ApiTag,
            });
        }

        var level0 = GetOrAddObject(GetOrAddObject(GetOrAddObject(config, "policy"), "levels"), "0");
        level0["statsUserUplink"] = true;
        level0["statsUserDownlink"] = true;
    }

    private static bool IsApiRule(JsonObject rule) =>
        AsString(rule["outboundTag"]) == ApiTag
        && rule["inboundTag"] is JsonArray tags
        && tags.Any(t => AsString(t) == ApiTag);

    private static JsonObject GetOrAddObject(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject existing) return existing;
        var created = new JsonObject();
        parent[key] = created;
        return created;
    }

    private static JsonArray GetOrAddArray(JsonObject parent, string key)
    {
        if (parent[key] is JsonArray existing) return existing;
        var created = new JsonArray();
        parent[key] = created;
        return created;
    }
}
