using API.Services;

namespace API.Tests.Unit;

public class RetiredSecretsTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RetiredSecret_ResolvesToItsUser()
    {
        var retired = new RetiredSecrets();
        retired.Retire("old", "user-1", T0);

        Assert.Equal("user-1", retired.Snapshot()["old"]);
    }

    [Fact]
    public void Purge_KeepsSecretsYoungerThanOneCollectionPeriod()
    {
        var retired = new RetiredSecrets();
        retired.Retire("old", "user-1", T0);

        retired.Purge(T0.AddMinutes(4).AddSeconds(59));

        Assert.True(retired.Snapshot().ContainsKey("old"));
    }

    [Fact]
    public void Purge_DropsSecretsAtLeastOneCollectionPeriodOld()
    {
        var retired = new RetiredSecrets();
        retired.Retire("old", "user-1", T0);
        retired.Retire("newer", "user-2", T0.AddMinutes(3));

        retired.Purge(T0.AddMinutes(5));

        var left = retired.Snapshot();
        Assert.False(left.ContainsKey("old"));
        Assert.True(left.ContainsKey("newer"));
    }
}
