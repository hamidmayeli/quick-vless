namespace API.Models;

public sealed class Admin
{
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string PasswordSalt { get; set; }
    public List<RefreshTokenEntry> RefreshTokens { get; set; } = [];
}

public sealed class RefreshTokenEntry
{
    public required string Token { get; set; }
    public required DateTime Expiry { get; set; }
}
