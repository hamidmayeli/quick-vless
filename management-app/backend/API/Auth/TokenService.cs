using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using API.Models;

namespace API.Auth;

public sealed class TokenService(IConfiguration config)
{
    private readonly string _secret = config["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret not configured");
    private readonly string _issuer = config["Jwt:Issuer"] ?? "easy-xray";
    private readonly int _accessMinutes = int.TryParse(config["Jwt:AccessTokenMinutes"], out var m) ? m : 60;
    private readonly int _refreshDays = int.TryParse(config["Jwt:RefreshTokenDays"], out var d) ? d : 30;

    public (string Hash, string Salt) HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var salt = Convert.ToBase64String(saltBytes);
        var hash = Convert.ToBase64String(
            Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, 100_000, HashAlgorithmName.SHA256, 32));
        return (hash, salt);
    }

    public bool VerifyPassword(string hash, string salt, string password)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var expected = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(hash));
    }

    public string CreateAccessToken(string username)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _issuer,
            claims: [new Claim(ClaimTypes.Name, username)],
            expires: DateTime.UtcNow.AddMinutes(_accessMinutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string Token, DateTime Expiry) CreateRefreshToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return (token, DateTime.UtcNow.AddDays(_refreshDays));
    }

    public bool ValidateRefreshToken(Admin admin, string token) =>
        admin.RefreshTokens.Any(rt => rt.Token == token && rt.Expiry > DateTime.UtcNow);
}
