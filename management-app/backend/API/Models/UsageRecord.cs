namespace API.Models;

public sealed class UsageRecord
{
    public required string UserId { get; set; }
    public required string Date { get; set; }
    public long Uplink { get; set; }
    public long Downlink { get; set; }
    public long Total { get; set; }
}
