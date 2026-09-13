using API.Storage;
using System.Globalization;

namespace API.Services;

public sealed class MetricsCollectorService(
    XrayService xray,
    UsageRepository usageRepo,
    UserRepository userRepo,
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
            await CollectAsync();
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Metrics collection failed");
        }
    }
}
