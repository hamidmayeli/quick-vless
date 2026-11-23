using API.Storage;
using System.Globalization;

namespace API.Services;

public sealed class MetricsCollectorService(
    XrayService xray,
    UsageRepository usageRepo,
    UserRepository userRepo,
    RetiredSecrets retired,
    ILogger<MetricsCollectorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var now = DateTime.UtcNow;
        var nextCollection = now.AddMinutes(5 - now.Minute % 5);
        nextCollection = new DateTime(nextCollection.Year, nextCollection.Month, nextCollection.Day,
            nextCollection.Hour, nextCollection.Minute, 0, DateTimeKind.Utc);
        if (nextCollection < now.AddMinutes(1))
            nextCollection = nextCollection.AddMinutes(5);

        await Task.Delay(nextCollection - now, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            CleanUp();
            await CollectAsync();
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private void CleanUp()
    {
        try
        {
            if (DateTime.Now.Hour == 0 && DateTime.Now.Minute <= 5)
                usageRepo.CleanUp();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Metrics clean up failed");
        }
    }

    private async Task CollectAsync()
    {
        try
        {
            var stats = await xray.QueryStatsAsync();
            if (stats.Count == 0) return;

            var users = await userRepo.GetAllAsync();

            // Retired (rotated) secrets first, so a current secret always wins on the off chance of a clash.
            var userById = retired.Snapshot().ToDictionary(kv => kv.Key, kv => kv.Value);

            foreach (var user in users)
                userById[user.Secret] = user.Id;

            var date = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

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

            // Only after a successful read: if Xray was unreachable the retired secrets are still needed.
            retired.Purge();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Metrics collection failed");
        }
    }
}
