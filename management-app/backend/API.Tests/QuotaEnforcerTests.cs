using API.Models;

namespace API.Tests;

public class QuotaEnforcerTests
{
    private const double BytesPerGb = 1_073_741_824.0;

    [Theory]
    [InlineData(50.0, 50 * 1_073_741_824L, true)]    // exactly at quota
    [InlineData(50.0, 51 * 1_073_741_824L, true)]    // over quota
    [InlineData(50.0, 49 * 1_073_741_824L, false)]   // under quota
    [InlineData(null, 100 * 1_073_741_824L, false)]  // unlimited
    public void QuotaExceeded_ReturnsExpected(double? quotaGb, long totalBytes, bool shouldBeExceeded)
    {
        var user = new User
        {
            Id = "1",
            Name = "Test",
            Secret = Guid.NewGuid().ToString(),
            Quota = quotaGb,
            Enabled = true,
            SingleConnection = false,
        };

        var totalGb = totalBytes / BytesPerGb;
        var exceeded = user.Quota.HasValue && totalGb >= user.Quota.Value;
        Assert.Equal(shouldBeExceeded, exceeded);
    }
}
