using System.ComponentModel;
using System.Diagnostics;

namespace API.Services.Xray;

public sealed record XrayConfigTestResult(bool Passed, string? Error);

public interface IXrayConfigTester
{
    Task<XrayConfigTestResult> TestAsync(string configPath, CancellationToken ct = default);
}

/// <summary>Checks a config with <c>xray run -test</c>, using the xray binary shipped in the app image.</summary>
public sealed class XrayBinaryConfigTester(XrayOptions options, ILogger<XrayBinaryConfigTester> logger) : IXrayConfigTester
{
    public async Task<XrayConfigTestResult> TestAsync(string configPath, CancellationToken ct = default)
    {
        var start = new ProcessStartInfo(options.BinaryPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "run", "-test", "-config", configPath }) start.ArgumentList.Add(arg);

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Win32Exception)
        {
            // Development machines usually have no xray binary; the image always does.
            logger.LogWarning("Xray binary {Binary} not found — skipping the config test", options.BinaryPath);
            return new XrayConfigTestResult(true, null);
        }
        if (process is null) return new XrayConfigTestResult(false, "Could not start xray to test the config.");

        using (process)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                return new XrayConfigTestResult(false, "xray run -test did not finish within 30 seconds.");
            }

            if (process.ExitCode == 0) return new XrayConfigTestResult(true, null);
            return new XrayConfigTestResult(false, Summarize(await stdout + "\n" + await stderr));
        }
    }

    /// <summary>Xray explains a rejected config on a "Failed to start: …" line.</summary>
    public static string Summarize(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var line = lines.LastOrDefault(l => l.StartsWith("Failed to start", StringComparison.Ordinal))
                   ?? lines.LastOrDefault()
                   ?? "xray rejected the config.";
        return line.Length > 500 ? line[..500] + "…" : line;
    }
}
