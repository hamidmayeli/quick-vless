using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration;

public sealed class UsageIntegrationTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public UsageIntegrationTests()
    {
        _client = _factory.CreateClient();
        SetupAuth().GetAwaiter().GetResult();
    }

    public void Dispose() => _factory.Dispose();

    private async Task SetupAuth()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "pass" });
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", json.GetProperty("access_token").GetString()!);
    }

    [Fact]
    public async Task GetUsage_EmptyState_ReturnsEmptyArray()
    {
        var response = await _client.GetAsync("/api/v1/usage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var records = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.Empty(records!);
    }

    [Fact]
    public async Task GetUsage_WithoutAuth_Returns401()
    {
        var noAuth = _factory.CreateClient();
        var response = await noAuth.GetAsync("/api/v1/usage");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
