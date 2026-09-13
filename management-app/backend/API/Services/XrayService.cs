using API.Models;

namespace API.Services;

public sealed class XrayService(IConfiguration config, ILogger<XrayService> logger)
{
    private readonly string _xrayBin = config["Xray:BinaryPath"] ?? "xray";
    private readonly string _configPath = config["Xray:ConfigPath"] ?? "/usr/local/etc/xray/config.json";
    private readonly string _statsServer = config["Xray:StatsServer"] ?? "127.0.0.1:10085";
    private readonly string _inboundTag = config["Xray:InboundTag"] ?? "vless-in";

    public async Task<bool> AddUserAsync(User user)
    {
        var json = $$"""{"id":"{{user.Secret}}","flow":"xtls-rprx-vision"}""";
        return await RunAsync("api", "command", "HandlerService.AddInbound",
            "--server", _statsServer,
            "-name", _inboundTag,
            "-user", json);
    }

    public async Task<bool> RemoveUserAsync(User user)
    {
        return await RunAsync("api", "command", "HandlerService.RemoveUser",
            "--server", _statsServer,
            "-tag", _inboundTag,
            "-email", user.Secret);
    }

    public async Task<Dictionary<string, long>> QueryStatsAsync()
    {
        var result = new Dictionary<string, long>();
        var (output, success) = await RunWithOutputAsync("api", "statsquery",
            "--server", _statsServer, "-pattern", "user");
        if (!success) return result;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // stat: name: "user>>>secret>>>traffic>>>downlink"  value: 12345
            var parts = line.Split(['"'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var nameParts = parts[1].Split(">>>", StringSplitOptions.RemoveEmptyEntries);
            if (nameParts.Length < 4) continue;
            var userId = nameParts[1];
            if (!long.TryParse(line.Split("value:").LastOrDefault()?.Trim(), out var bytes)) continue;
            result.TryGetValue(userId, out var existing);
            result[userId] = existing + bytes;
        }
        return result;
    }

    private async Task<bool> RunAsync(params string[] args)
    {
        var (_, success) = await RunWithOutputAsync(args);
        return success;
    }

    private async Task<(string Output, bool Success)> RunWithOutputAsync(params string[] args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(_xrayBin)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (output, proc.ExitCode == 0);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Xray CLI invocation failed: {Args}", string.Join(' ', args));
            return (string.Empty, false);
        }
    }
}
