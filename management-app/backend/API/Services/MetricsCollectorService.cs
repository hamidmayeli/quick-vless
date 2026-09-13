using API.Storage;

namespace API.Services;

public sealed class MetricsCollectorService(
    XrayService xray,
    UsageRepository usageRepo,
    UserRepository userRepo,
    ILogger<MetricsCollectorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(300), stoppingToken);
            await CollectAsync();
        }
    }

    private async Task CollectAsync()
    {
        try
        {
            var stats = await xray.QueryStatsAsync();
            if (stats.Count == 0) return;

            var users = await userRepo.GetAllAsync();
            var userById = users.ToDictionary(u => u.Secret, u => u.Id);
            var date = DateTime.UtcNow.ToString("yyyyMMdd");

            foreach (var (secret, totalBytes) in stats)
            {
                if (!userById.TryGetValue(secret, out var userId)) continue;
                await usageRepo.AppendAsync(new Models.UsageRecord
                {
                    UserId = userId,
                    Date = date,
                    Uplink = 0,
                    Downlink = 0,
                    Total = totalBytes,
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Metrics collection failed");
        }
    }
}
