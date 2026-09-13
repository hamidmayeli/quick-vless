using System.Text.Json;
using API.Storage;

namespace API.Services;

public sealed class XrayConfigSyncService(
    UserRepository userRepo,
    XrayService xray,
    IConfiguration config,
    ILogger<XrayConfigSyncService> logger) : IHostedService
{
    private readonly string _configPath = config["Xray:ConfigPath"] ?? "/usr/local/etc/xray/config.json";
    private readonly string _inboundTag = config["Xray:InboundTag"] ?? "vless-in";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var users = await userRepo.GetAllAsync();
            var enabled = users.Where(u => u.Enabled).ToList();

            if (!File.Exists(_configPath))
            {
                logger.LogWarning("Xray config not found at {Path}. Skipping startup sync.", _configPath);
                return;
            }

            var json = await File.ReadAllTextAsync(_configPath, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.Clone();

            // Build new clients list
            var clients = enabled.Select(u => new { id = u.Secret, flow = "xtls-rprx-vision" }).ToArray();

            // Patch inbound clients in config file
            var options = new JsonSerializerOptions { WriteIndented = true };
            var config2 = PatchInboundClients(root, _inboundTag, clients, options);
            await File.WriteAllTextAsync(_configPath, config2, cancellationToken);

            logger.LogInformation("Xray config synced with {Count} enabled users.", enabled.Count);

            // Also hot-reload via gRPC for any users that should be active
            foreach (var user in enabled)
                await xray.AddUserAsync(user);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Xray startup sync failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string PatchInboundClients(JsonElement root, string tag, object clients, JsonSerializerOptions opts)
    {
        // Serialize the full config, then patch the clients array for the matching inbound tag
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(root.GetRawText())!;
        if (!dict.TryGetValue("inbounds", out var inboundsEl)) return root.GetRawText();

        var inbounds = JsonSerializer.Deserialize<List<JsonElement>>(inboundsEl.GetRawText())!;
        var newInbounds = inbounds.Select(inbound =>
        {
            if (!inbound.TryGetProperty("tag", out var tagEl) || tagEl.GetString() != tag)
                return inbound;

            var inDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(inbound.GetRawText())!;
            if (inDict.TryGetValue("settings", out var settingsEl))
            {
                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(settingsEl.GetRawText())!;
                settings["clients"] = JsonSerializer.SerializeToElement(clients);
                inDict["settings"] = JsonSerializer.SerializeToElement(settings);
            }
            return JsonSerializer.SerializeToElement(inDict);
        }).ToList();

        dict["inbounds"] = JsonSerializer.SerializeToElement(newInbounds);
        return JsonSerializer.Serialize(dict, opts);
    }
}
