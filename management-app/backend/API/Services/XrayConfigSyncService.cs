using API.Services.Xray;

namespace API.Services;

public sealed class XrayConfigSyncService(
    XrayRuntime runtime,
    ILogger<XrayConfigSyncService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await runtime.InitializeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Xray startup sync failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
