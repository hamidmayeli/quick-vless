using API.Models;

namespace API.Storage;

public sealed class UsageRepository(IConfiguration config)
{
    private readonly string _dir = config["Storage:UsageHistoryDir"] ?? "/data/usage_history";

    public async Task AppendAsync(UsageRecord record)
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, $"{record.Date}.csv");
        var line = $"{record.UserId},{record.Date},{record.Uplink},{record.Downlink},{record.Total}";
        await File.AppendAllTextAsync(file, line + Environment.NewLine);
    }

    public async Task<List<UsageRecord>> GetAllAsync()
    {
        var records = new List<UsageRecord>();
        if (!Directory.Exists(_dir)) return records;
        foreach (var file in Directory.GetFiles(_dir, "*.csv").OrderBy(f => f))
        {
            var lines = await File.ReadAllLinesAsync(file);
            foreach (var line in lines)
            {
                var parts = line.Split(',');
                if (parts.Length < 5) continue;
                records.Add(new UsageRecord
                {
                    UserId = parts[0],
                    Date = parts[1],
                    Uplink = long.TryParse(parts[2], out var up) ? up : 0,
                    Downlink = long.TryParse(parts[3], out var down) ? down : 0,
                    Total = long.TryParse(parts[4], out var total) ? total : 0,
                });
            }
        }
        return records;
    }

    public async Task<Dictionary<string, long>> GetCumulativeTotalsByUserAsync()
    {
        var records = await GetAllAsync();
        return records
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Total));
    }
}
