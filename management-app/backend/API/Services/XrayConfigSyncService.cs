using API.Storage;

namespace API.Services;

public sealed class XrayConfigSyncService(
    UserRepository userRepo,
    XrayService xray,
    XrayConfigFileService configFile,
    ILogger<XrayConfigSyncService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await configFile.SyncAsync(cancellationToken);

            var users = await userRepo.GetAllAsync();
            foreach (var user in users.Where(u => u.Enabled))
                await xray.AddUserAsync(user);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Xray startup sync failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
