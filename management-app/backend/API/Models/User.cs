namespace API.Models;

public sealed class User
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Secret { get; set; }
    public long? Quota { get; set; }
    public DateOnly? Expiry { get; set; }
    public bool SingleConnection { get; set; }
    public bool Enabled { get; set; }
}
