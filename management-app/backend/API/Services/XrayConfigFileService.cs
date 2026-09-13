using System.Text.Json;
using API.Storage;

namespace API.Services;

public sealed class XrayConfigFileService(
    UserRepository userRepo,
    IConfiguration config,
    ILogger<XrayConfigFileService> logger)
{
    private readonly string _configPath = config["Xray:ConfigPath"] ?? "/usr/local/etc/xray/config.json";
    private readonly string _inboundTag = config["Xray:InboundTag"] ?? "vless-in";
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task SyncAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_configPath))
            {
                logger.LogWarning("Xray config not found at {Path}. Skipping sync.", _configPath);
                return;
            }

            var users = await userRepo.GetAllAsync();
            var enabled = users.Where(u => u.Enabled).ToList();

            var json = await File.ReadAllTextAsync(_configPath, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.Clone();

            var clients = enabled.Select(u => new
            {
                id = u.Secret,
                email = u.Name,
                flow = "xtls-rprx-vision",
            }).ToArray();

            var opts = new JsonSerializerOptions { WriteIndented = true };
            var patched = PatchInboundClients(root, _inboundTag, clients, opts);
            await File.WriteAllTextAsync(_configPath, patched, ct);

            logger.LogInformation("Xray config synced with {Count} enabled users.", enabled.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string PatchInboundClients(JsonElement root, string tag, object clients, JsonSerializerOptions opts)
    {
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
