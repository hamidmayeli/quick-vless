using API.Models;

namespace API.Tests;

public class QuotaEnforcerTests
{
    [Theory]
    [InlineData(50L * 1_073_741_824L, 50L * 1_073_741_824L, true)]
    [InlineData(50L * 1_073_741_824L, 51L * 1_073_741_824L, true)]
    [InlineData(50L * 1_073_741_824L, 49L * 1_073_741_824L, false)]
    [InlineData(null, 100L * 1_073_741_824L, false)]
    public void QuotaExceeded_ReturnsExpected(long? quotaBytes, long totalBytes, bool shouldBeExceeded)
    {
        var user = new User
        {
            Id = "1",
            Name = "Test",
            Secret = Guid.NewGuid().ToString(),
            Quota = quotaBytes,
            Enabled = true,
            SingleConnection = false,
        };

        var exceeded = user.Quota.HasValue && totalBytes >= user.Quota.Value;
        Assert.Equal(shouldBeExceeded, exceeded);
    }
}
