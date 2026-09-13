using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration;

public sealed class ConfigIntegrationTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _client;
    private string _userId = string.Empty;

    public ConfigIntegrationTests()
    {
        _client = _factory.CreateClient();
        Setup().GetAwaiter().GetResult();
    }

    public void Dispose() => _factory.Dispose();

    private async Task Setup()
    {
        var loginRes = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "pass" });
        var tokens = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString()!);

        var userRes = await _client.PostAsJsonAsync("/api/v1/users", new
        {
            name = "ConfigUser",
            quota = (double?)null,
            expiry = (string?)null,
            single_connection = false,
            enabled = true,
        });
        var user = await userRes.Content.ReadFromJsonAsync<JsonElement>();
        _userId = user.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task GetConfig_ByUserId_IsPublic_ReturnsVlessUrl()
    {
        // Public endpoint — no auth required
        var noAuth = _factory.CreateClient();
        var response = await noAuth.GetAsync($"/api/v1/config/{_userId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var url = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("vless://", url);
    }

    [Fact]
    public async Task GetConfig_ByUserId_ReturnsPlainText()
    {
        var response = await _client.GetAsync($"/api/v1/config/{_userId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/plain", response.Content.Headers.ContentType?.MediaType);
        var url = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("vless://", url);
    }

    [Fact]
    public async Task GetConfig_ByNonExistentUserId_Returns404()
    {
        var response = await _client.GetAsync("/api/v1/config/nonexistent-user-id");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetConfig_ByDisabledUser_Returns404()
    {
        var createRes = await _client.PostAsJsonAsync("/api/v1/users", new
        {
            name = "DisabledUser",
            quota = (double?)null,
            expiry = (string?)null,
            single_connection = false,
            enabled = false,
        });
        var created = await createRes.Content.ReadFromJsonAsync<JsonElement>();
        var disabledId = created.GetProperty("id").GetString()!;

        var noAuth = _factory.CreateClient();
        var response = await noAuth.GetAsync($"/api/v1/config/{disabledId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
