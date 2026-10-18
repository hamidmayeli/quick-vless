using System.Text.Json.Serialization;

namespace API.Models;

public enum AdminRole
{
    Admin = 0,
    SuperAdmin = 1,
}

public sealed class Admin
{
    public required string Username { get; set; }
    public AdminRole Role { get; set; }
    // Null until the admin sets a password on their first login.
    public string? PasswordHash { get; set; }
    public string? PasswordSalt { get; set; }
    public List<RefreshTokenEntry> RefreshTokens { get; set; } = [];

    [JsonIgnore]
    public bool IsPending => PasswordHash is null;
}

public sealed class RefreshTokenEntry
{
    public required string Token { get; set; }
    public required DateTime Expiry { get; set; }
}
