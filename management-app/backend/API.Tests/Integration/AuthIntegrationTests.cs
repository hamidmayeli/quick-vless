using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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
    public async Task FirstLogin_NoAdmins_CreatesAdminAndReturnsTokens()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "secret123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("access_token", out var at) && at.GetString() is not null);
        Assert.True(json.TryGetProperty("refresh_token", out var rt) && rt.GetString() is not null);
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
    public async Task Refresh_ValidToken_ReturnsNewTokens()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "pass" });
        var tokens = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = tokens.GetProperty("refresh_token").GetString()!;

        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { username = "admin", refresh_token = refreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newTokens = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(newTokens.TryGetProperty("access_token", out _));
    }

    [Fact]
    public async Task Refresh_InvalidToken_Returns401()
    {
        await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "pass" });

        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { username = "admin", refresh_token = "invalid-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_UnknownUser_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { username = "nobody", refresh_token = "any-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
