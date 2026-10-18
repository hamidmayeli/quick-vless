namespace API.Services.Xray;

public interface IXrayRestartMonitor
{
    /// <summary>False when restart confirmation is turned off (no launcher, e.g. CI).</summary>
    bool IsEnabled { get; }

    /// <summary>Waits until Xray runs the config with this structure hash and its API answers.</summary>
    Task<bool> WaitForAsync(string structureHash, CancellationToken ct = default);
}

/// <summary>
/// Watches running.sha256, which xray-run.sh writes once Xray has stayed up with a config,
/// and probes the gRPC API to make sure that Xray is the one answering.
/// </summary>
public sealed class FileRestartMonitor(XrayOptions options, IXrayClient xray) : IXrayRestartMonitor
{
    public bool IsEnabled => options.RestartConfirmTimeout > TimeSpan.Zero;

    public async Task<bool> WaitForAsync(string structureHash, CancellationToken ct = default)
    {
        if (!IsEnabled) return true;
        var deadline = DateTime.UtcNow + options.RestartConfirmTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (XrayRuntime.ReadTrimmed(options.RunningHashPath) == structureHash && await xray.ProbeAsync())
                return true;
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        return false;
    }
}
