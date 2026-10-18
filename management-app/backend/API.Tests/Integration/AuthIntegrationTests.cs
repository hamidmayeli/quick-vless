using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using API.Auth;
using API.Models;

namespace API.Tests.Integration;

public sealed class AuthIntegrationTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public AuthIntegrationTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task FirstLogin_NoAdmins_CreatesSuperAdminAndReturnsTokens()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "secret123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("access_token", out var at) && at.GetString() is not null);
        Assert.Equal(AdminRoles.SuperAdmin, TestAuth.ReadClaim(at.GetString()!, ClaimTypes.Role));
        Assert.False(json.TryGetProperty("refresh_token", out _));
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("refresh_token=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FirstLogin_ConcurrentDifferentUsernames_OnlyOneSuperAdminCreated()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            TestAuth.PostLoginAsync(_factory.CreateClient(), $"racer{i}", "pass")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
    }

    [Theory]
    [InlineData("", "pass")]
    [InlineData("   ", "pass")]
    [InlineData("admin", "")]
    [InlineData("admin", "   ")]
    public async Task Login_BlankUsernameOrPassword_Returns400(string username, string password)
    {
        var response = await TestAuth.PostLoginAsync(_client, username, password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        // Bootstrap admin
        await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "correct" });

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownUserWhenAdminsExist_Returns401()
    {
        // Bootstrap admin
        await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "pass" });

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "hacker", password = "pass" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SecondLoginWithCorrectPassword_ReturnsTokens()
    {
        const string pwd = "my-password";
        // Create admin via first login
        await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = pwd });

        // Log in again with same credentials
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = pwd });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PendingAdmin_FirstLogin_SetsPasswordAndReturnsAdminRoleToken()
    {
        var super = await TestAuth.CreateSuperAdminClientAsync(_factory);
        await TestAuth.CreateAdminAsync(super, "alice");

        var token = await TestAuth.LoginAsync(_client, "alice", "alice-pass");

        Assert.Equal("alice", TestAuth.ReadClaim(token, ClaimTypes.Name));
        Assert.Equal(AdminRoles.Admin, TestAuth.ReadClaim(token, ClaimTypes.Role));
        var admins = await super.GetFromJsonAsync<JsonElement[]>("/api/v1/admins");
        var alice = Assert.Single(admins!, a => a.GetProperty("username").GetString() == "alice");
        Assert.Equal("active", alice.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PendingAdmin_SecondLoginSamePassword_Returns200()
    {
        var super = await TestAuth.CreateSuperAdminClientAsync(_factory);
        await TestAuth.CreateAdminAsync(super, "alice");
        await TestAuth.LoginAsync(_client, "alice", "alice-pass");

        var response = await TestAuth.PostLoginAsync(_client, "alice", "alice-pass");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PendingAdmin_SecondLoginDifferentPassword_Returns401()
    {
        var super = await TestAuth.CreateSuperAdminClientAsync(_factory);
        await TestAuth.CreateAdminAsync(super, "alice");
        await TestAuth.LoginAsync(_client, "alice", "alice-pass");

        var response = await TestAuth.PostLoginAsync(_client, "alice", "attacker-pass");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PendingAdmin_BlankPassword_Returns400AndStaysPending()
    {
        var super = await TestAuth.CreateSuperAdminClientAsync(_factory);
        await TestAuth.CreateAdminAsync(super, "alice");

        var response = await TestAuth.PostLoginAsync(_client, "alice", "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var admins = await super.GetFromJsonAsync<JsonElement[]>("/api/v1/admins");
        var alice = Assert.Single(admins!, a => a.GetProperty("username").GetString() == "alice");
        Assert.Equal("pending", alice.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PendingAdmin_ConcurrentClaimsDifferentPasswords_ExactlyOneSucceeds()
    {
        var super = await TestAuth.CreateSuperAdminClientAsync(_factory);
        await TestAuth.CreateAdminAsync(super, "alice");

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            TestAuth.PostLoginAsync(_factory.CreateClient(), "alice", $"password-{i}")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Login_UsernameInDifferentCase_TokenUsesStoredUsername()
    {
        var super = await TestAuth.CreateSuperAdminClientAsync(_factory);
        await TestAuth.CreateAdminAsync(super, "alice");

        var token = await TestAuth.LoginAsync(_client, "ALICE", "alice-pass");

        Assert.Equal("alice", TestAuth.ReadClaim(token, ClaimTypes.Name));
    }

    [Fact]
    public async Task LegacyAdminsFile_WithoutRole_FirstAdminIsPromotedToSuperAdmin()
    {
        using var factory = new CustomWebApplicationFactory();
        var (hash, salt) = CreateTokenService().HashPassword("legacy-pass");
        factory.SeedFile("admins.json", JsonSerializer.Serialize(new[]
        {
            new { username = "legacy", password_hash = hash, password_salt = salt, refresh_tokens = Array.Empty<object>() },
        }));

        var client = await TestAuth.CreateClientAsAsync(factory, "legacy", "legacy-pass");

        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        Assert.Equal(AdminRoles.SuperAdmin, TestAuth.ReadClaim(token, ClaimTypes.Role));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/admins")).StatusCode);
    }

    [Fact]
    public async Task DeletedAdmin_ExistingAccessToken_Returns401()
    {
        var super = await TestAuth.CreateSuperAdminClientAsync(_factory);
        var alice = await TestAuth.CreateAdminClientAsync(_factory, super, "alice");
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/api/v1/users")).StatusCode);

        await super.DeleteAsync("/api/v1/admins/alice");

        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task DeletedAdmin_Refresh_Returns401()
    {
        var super = await TestAuth.CreateSuperAdminClientAsync(_factory);
        await TestAuth.CreateAdminAsync(super, "alice");
        var login = await TestAuth.PostLoginAsync(_client, "alice", "alice-pass");
        var refreshToken = TestAuth.RefreshTokenFrom(login);

        await super.DeleteAsync("/api/v1/admins/alice");
        _client.DefaultRequestHeaders.Add("Cookie", $"refresh_token={refreshToken}");
        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { username = "alice" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AccessToken_RoleClaimIsReplacedWithStoredRole()
    {
        var super = await TestAuth.CreateSuperAdminClientAsync(_factory);
        await TestAuth.CreateAdminClientAsync(_factory, super, "alice");

        // A validly signed token claiming super_admin for a normal admin must not grant super-admin endpoints.
        var forged = _factory.CreateClient();
        forged.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateSignedToken("alice", AdminRole.SuperAdmin));

        Assert.Equal(HttpStatusCode.Forbidden, (await forged.GetAsync("/api/v1/admins")).StatusCode);
    }

    [Fact]
    public async Task Refresh_ValidToken_ReturnsNewTokens()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "pass" });
        var refreshToken = Assert.Single(loginResponse.Headers.GetValues("Set-Cookie"))
            .Split(';')[0].Split('=', 2)[1];
        _client.DefaultRequestHeaders.Add("Cookie", $"refresh_token={refreshToken}");

        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { username = "admin" });
        response.Headers.TryGetValues("Set-Cookie", out var cookies);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newTokens = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(newTokens.TryGetProperty("access_token", out var accessToken));
        Assert.Equal(AdminRoles.SuperAdmin, TestAuth.ReadClaim(accessToken.GetString()!, ClaimTypes.Role));
        Assert.NotEqual(refreshToken, Assert.Single(cookies!).Split(';')[0].Split('=', 2)[1]);
    }

    [Fact]
    public async Task Refresh_UsedTokenIsRevoked()
    {
        var loginResponse = await TestAuth.PostLoginAsync(_client, "admin", "pass");
        var refreshToken = TestAuth.RefreshTokenFrom(loginResponse);
        _client.DefaultRequestHeaders.Add("Cookie", $"refresh_token={refreshToken}");

        var first = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { username = "admin" });
        var second = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { username = "admin" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [Fact]
    public async Task Refresh_InvalidToken_Returns401()
    {
        await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "pass" });

        _client.DefaultRequestHeaders.Add("Cookie", "refresh_token=invalid-token");
        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { username = "admin" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_UnknownUser_Returns401()
    {
        _client.DefaultRequestHeaders.Add("Cookie", "refresh_token=any-token");
        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { username = "nobody" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AccessToken_ForUnknownAdmin_Returns401()
    {
        // A token signed with the right key but for an admin that does not exist in admins.json.
        await TestAuth.CreateSuperAdminClientAsync(_factory);
        var ghost = _factory.CreateClient();
        ghost.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateSignedToken("ghost", AdminRole.SuperAdmin));

        Assert.Equal(HttpStatusCode.Unauthorized, (await ghost.GetAsync("/api/v1/users")).StatusCode);
    }

    private static string CreateSignedToken(string username, AdminRole role) =>
        CreateTokenService().CreateAccessToken(username, role);

    // Same signing settings as the app under test.
    private static TokenService CreateTokenService() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = CustomWebApplicationFactory.JwtSecret,
                ["Jwt:Issuer"] = CustomWebApplicationFactory.JwtIssuer,
            })
            .Build());
}
