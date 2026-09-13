using API.Storage;

namespace API.Services;

public sealed class QuotaEnforcerService(
    UserRepository userRepo,
    UsageRepository usageRepo,
    XrayService xray,
    XrayConfigFileService configFile,
    ILogger<QuotaEnforcerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(300), stoppingToken);
            await EnforceAsync();
        }
    }

    private async Task EnforceAsync()
    {
        var changed = false;
        try
        {
            var users = await userRepo.GetAllAsync();
            var totals = await usageRepo.GetCumulativeTotalsByUserAsync();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            foreach (var user in users.Where(u => u.Enabled))
            {
                bool shouldDisable = false;

                if (user.Expiry.HasValue && today >= user.Expiry.Value)
                {
                    logger.LogInformation("User {Name} ({Id}) expired on {Expiry}. Disabled.",
                        user.Name, user.Id, user.Expiry);
                    shouldDisable = true;
                }
                else if (user.Quota.HasValue && totals.TryGetValue(user.Id, out var totalBytes))
                {
                    if (totalBytes >= user.Quota.Value)
                    {
                        logger.LogInformation("User {Name} ({Id}) quota exceeded ({TotalBytes}/{Quota} bytes). Disabled.",
                            user.Name, user.Id, totalBytes, user.Quota);
                        shouldDisable = true;
                    }
                }

                if (shouldDisable)
                {
                    user.Enabled = false;
                    await userRepo.UpdateAsync(user);
                    await xray.RemoveUserAsync(user);
                    changed = true;
                }
            }

            if (changed)
                await configFile.SyncAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Quota enforcement failed");
        }
    }
}
