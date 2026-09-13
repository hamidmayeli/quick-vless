using Microsoft.Extensions.Configuration;
using API.Storage;
using API.Models;

namespace API.Tests;

public class UsageRepositoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    private UsageRepository CreateRepo()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:UsageHistoryDir"] = _tempDir,
            })
            .Build();
        return new UsageRepository(config);
    }

    [Fact]
    public async Task AppendAndGetAll_ReturnsAppendedRecords()
    {
        var repo = CreateRepo();
        var record = new UsageRecord { UserId = "u1", Date = "20260901", Uplink = 100, Downlink = 500, Total = 600 };
        await repo.AppendAsync(record);

        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("u1", all[0].UserId);
        Assert.Equal(600, all[0].Total);
    }

    [Fact]
    public async Task AppendWithTimestamp_UsesDateForFileAndPreservesTimestampInLine()
    {
        var repo = CreateRepo();
        var timestamp = "2026-09-01T11:55:00.0000000Z";
        await repo.AppendAsync(new UsageRecord { UserId = "u1", Date = timestamp, Uplink = 0, Downlink = 0, Total = 600 });

        var file = Path.Combine(_tempDir, "20260901.csv");
        Assert.True(File.Exists(file));
        Assert.Equal($"u1,{timestamp},0,0,600", (await File.ReadAllLinesAsync(file))[0]);
    }

    [Fact]
    public async Task GetCumulativeTotals_SumsCorrectly()
    {
        var repo = CreateRepo();
        await repo.AppendAsync(new UsageRecord { UserId = "u1", Date = "20260901", Uplink = 0, Downlink = 0, Total = 1000 });
        await repo.AppendAsync(new UsageRecord { UserId = "u1", Date = "20260902", Uplink = 0, Downlink = 0, Total = 2000 });
        await repo.AppendAsync(new UsageRecord { UserId = "u2", Date = "20260901", Uplink = 0, Downlink = 0, Total = 500 });

        var totals = await repo.GetCumulativeTotalsByUserAsync();
        Assert.Equal(3000, totals["u1"]);
        Assert.Equal(500, totals["u2"]);
    }

    [Fact]
    public async Task GetAll_EmptyDir_ReturnsEmpty()
    {
        var repo = CreateRepo();
        var all = await repo.GetAllAsync();
        Assert.Empty(all);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
