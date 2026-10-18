using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using API.Auth;
using API.Models;

namespace API.Tests.Unit;

public class TokenServiceTests
{
    private static TokenService CreateService() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "unit-test-secret-key-at-least-32-chars!!",
                ["Jwt:Issuer"] = "easy-xray-unit",
            })
            .Build());

    [Theory]
    [InlineData(AdminRole.SuperAdmin, "super_admin")]
    [InlineData(AdminRole.Admin, "admin")]
    public void CreateAccessToken_ContainsNameAndRoleClaims(AdminRole role, string expectedRole)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(CreateService().CreateAccessToken("alice", role));

        Assert.Equal("alice", jwt.Claims.Single(c => c.Type == ClaimTypes.Name).Value);
        Assert.Equal(expectedRole, jwt.Claims.Single(c => c.Type == ClaimTypes.Role).Value);
    }

    [Fact]
    public void HashThenVerify_CorrectPassword_ReturnsTrue()
    {
        var service = CreateService();
        var (hash, salt) = service.HashPassword("correct horse");

        Assert.True(service.VerifyPassword(hash, salt, "correct horse"));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var service = CreateService();
        var (hash, salt) = service.HashPassword("correct horse");

        Assert.False(service.VerifyPassword(hash, salt, "battery staple"));
    }

    [Fact]
    public void HashPassword_SamePasswordTwice_UsesDifferentSalts()
    {
        var service = CreateService();

        var first = service.HashPassword("same");
        var second = service.HashPassword("same");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void ValidateRefreshToken_OnlyAcceptsKnownUnexpiredTokens()
    {
        var admin = new Admin
        {
            Username = "alice",
            RefreshTokens =
            [
                new() { Token = "valid", Expiry = DateTime.UtcNow.AddDays(1) },
                new() { Token = "expired", Expiry = DateTime.UtcNow.AddMinutes(-1) },
            ],
        };
        var service = CreateService();

        Assert.True(service.ValidateRefreshToken(admin, "valid"));
        Assert.False(service.ValidateRefreshToken(admin, "expired"));
        Assert.False(service.ValidateRefreshToken(admin, "unknown"));
    }
}
