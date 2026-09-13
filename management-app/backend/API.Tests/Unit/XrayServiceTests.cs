using API.Services;
using Xray.App.Stats.Command;

namespace API.Tests.Unit;

public class XrayServiceTests
{
    [Fact]
    public void ParseStats_EmptyList_ReturnsEmpty()
    {
        var result = XrayService.ParseStats([]);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseStats_SingleUplink_ReturnsValue()
    {
        var stats = new[] { new Stat { Name = "user>>>abc>>>traffic>>>uplink", Value = 1000 } };
        var result = XrayService.ParseStats(stats);
        Assert.Equal(1000, result["abc"]);
    }

    [Fact]
    public void ParseStats_UplinkAndDownlink_SumsToTotal()
    {
        var stats = new[]
        {
            new Stat { Name = "user>>>abc>>>traffic>>>uplink", Value = 1000 },
            new Stat { Name = "user>>>abc>>>traffic>>>downlink", Value = 2000 },
        };
        var result = XrayService.ParseStats(stats);
        Assert.Single(result);
        Assert.Equal(3000, result["abc"]);
    }

    [Fact]
    public void ParseStats_MultipleUsers_KeyedByEmail()
    {
        var stats = new[]
        {
            new Stat { Name = "user>>>user1>>>traffic>>>uplink", Value = 100 },
            new Stat { Name = "user>>>user2>>>traffic>>>uplink", Value = 200 },
        };
        var result = XrayService.ParseStats(stats);
        Assert.Equal(2, result.Count);
        Assert.Equal(100, result["user1"]);
        Assert.Equal(200, result["user2"]);
    }

    [Fact]
    public void ParseStats_MalformedEntry_IsSkipped()
    {
        var stats = new[]
        {
            new Stat { Name = "bad-format", Value = 999 },
            new Stat { Name = "user>>>good>>>traffic>>>uplink", Value = 100 },
        };
        var result = XrayService.ParseStats(stats);
        Assert.Single(result);
        Assert.Equal(100, result["good"]);
    }

    [Fact]
    public void ParseStats_ZeroValueEntry_IsIncluded()
    {
        var stats = new[] { new Stat { Name = "user>>>abc>>>traffic>>>uplink", Value = 0 } };
        var result = XrayService.ParseStats(stats);
        Assert.Equal(0, result["abc"]);
    }
}
