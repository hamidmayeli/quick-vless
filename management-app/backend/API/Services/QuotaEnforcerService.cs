using API.Storage;

namespace API.Services;

public sealed class QuotaEnforcerService(
    UserRepository userRepo,
    UsageRepository usageRepo,
    XrayService xray,
    ILogger<QuotaEnforcerService> logger) : BackgroundService
{
    private const double BytesPerGb = 1_073_741_824.0;

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
        try
        {
            var users = await userRepo.GetAllAsync();
            var totals = await usageRepo.GetCumulativeTotalsByUserAsync();

            foreach (var user in users.Where(u => u.Enabled && u.Quota.HasValue))
            {
                if (!totals.TryGetValue(user.Id, out var totalBytes)) continue;
                var totalGb = totalBytes / BytesPerGb;
                if (totalGb < user.Quota!.Value) continue;

                user.Enabled = false;
                await userRepo.UpdateAsync(user);
                await xray.RemoveUserAsync(user);
                logger.LogInformation("User {Name} ({Id}) quota exceeded ({TotalGb:F2}/{Quota} GB). Disabled.",
                    user.Name, user.Id, totalGb, user.Quota);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Quota enforcement failed");
        }
    }
}
