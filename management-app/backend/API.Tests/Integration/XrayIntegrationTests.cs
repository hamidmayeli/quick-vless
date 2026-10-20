using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration;

public sealed class XrayIntegrationTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new CustomWebApplicationFactory().WithProfiles();
    private readonly HttpClient _super;

    public XrayIntegrationTests()
    {
        _super = TestAuth.CreateSuperAdminClientAsync(_factory).GetAwaiter().GetResult();
    }

    public void Dispose() => _factory.Dispose();

    private Task<HttpResponseMessage> ActivateAsync(HttpClient client, string name) =>
        client.PutAsJsonAsync("/api/v1/xray/active-profile", new { name });

    private async Task<string> CreateEnabledUserAsync(string name)
    {
        var response = await _super.PostAsJsonAsync("/api/v1/users", new
        {
            name,
            quota = (long?)null,
            expiry = (string?)null,
            single_connection = false,
            enabled = true,
        });
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task GetProfiles_ListsEveryProfileWithItsStatus()
    {
        var state = await _super.GetFromJsonAsync<JsonElement>("/api/v1/xray/profiles");

        Assert.Equal("profiles", state.GetProperty("mode").GetString());
        Assert.Equal(TestProfiles.Reality, state.GetProperty("active").GetString());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("applied").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("last_error").ValueKind);
        var profiles = state.GetProperty("profiles").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!);
        Assert.Equal(new[] { TestProfiles.Broken, TestProfiles.WebSocket, TestProfiles.Reality }, profiles.Keys.Order().ToArray());
        Assert.True(profiles[TestProfiles.Reality].GetProperty("active").GetBoolean());
        Assert.True(profiles[TestProfiles.WebSocket].GetProperty("valid").GetBoolean());
        Assert.False(profiles[TestProfiles.Broken].GetProperty("valid").GetBoolean());
        Assert.Contains("YOUR_PRIVATE_KEY", profiles[TestProfiles.Broken].GetProperty("errors")[0].GetString());
        var inbound = profiles[TestProfiles.WebSocket].GetProperty("inbounds")[0];
        Assert.Equal("vless-cf", inbound.GetProperty("tag").GetString());
        Assert.Equal("ws", inbound.GetProperty("network").GetString());
        Assert.Equal("tls", inbound.GetProperty("security").GetString());
        Assert.Equal("", inbound.GetProperty("flow").GetString());
    }

    [Fact]
    public async Task GetProfiles_NeverReturnsPrivateKeys()
    {
        var body = await _super.GetStringAsync("/api/v1/xray/profiles");

        Assert.DoesNotContain("test-private-key", body);
    }

    [Fact]
    public async Task XrayEndpoints_NormalAdminGets403_AnonymousGets401()
    {
        var alice = await TestAuth.CreateAdminClientAsync(_factory, _super, "alice");
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/v1/xray/profiles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ActivateAsync(alice, TestProfiles.WebSocket)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsync("/api/v1/xray/active-profile/reapply", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/xray/profiles")).StatusCode);
    }

    [Fact]
    public async Task Activate_ValidProfile_ConfigLinksUseTheNewTemplate()
    {
        var userId = await CreateEnabledUserAsync("Alice");
        var before = await _factory.CreateClient().GetStringAsync($"/api/v1/config/{userId}");

        var response = await ActivateAsync(_super, TestProfiles.WebSocket);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestProfiles.WebSocket, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("active").GetString());
        var after = await _factory.CreateClient().GetStringAsync($"/api/v1/config/{userId}");
        Assert.Contains("type=tcp&security=reality", before);
        Assert.Contains("type=ws", after);
        Assert.EndsWith("#Alice", after);
        var runtimeConfig = await File.ReadAllTextAsync(Path.Combine(_factory.RuntimeDir, "config.json"));
        Assert.Contains("\"vless-cf\"", runtimeConfig);
        Assert.Contains($"\"xray_active_profile\": \"{TestProfiles.WebSocket}\"",
            await File.ReadAllTextAsync(Path.Combine(_factory.DataDir, "settings.json")));
    }

    [Fact]
    public async Task Activate_InvalidProfile_Returns400WithErrors()
    {
        var response = await ActivateAsync(_super, TestProfiles.Broken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("YOUR_PRIVATE_KEY", body.GetProperty("errors")[0].GetString());
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("../vless-reality")]
    public async Task Activate_UnknownProfile_Returns404(string name)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await ActivateAsync(_super, name)).StatusCode);
    }

    [Fact]
    public async Task Reapply_ActiveProfile_Returns200()
    {
        var response = await _super.PostAsync("/api/v1/xray/active-profile/reapply", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LegacyMode_ReportsLegacyAndRefusesActivation()
    {
        using var legacy = new CustomWebApplicationFactory();
        var super = await TestAuth.CreateSuperAdminClientAsync(legacy);

        var state = await super.GetFromJsonAsync<JsonElement>("/api/v1/xray/profiles");
        var activate = await ActivateAsync(super, TestProfiles.Reality);

        Assert.Equal("legacy", state.GetProperty("mode").GetString());
        Assert.Empty(state.GetProperty("profiles").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, activate.StatusCode);
        Assert.Contains("install.sh --upgrade", (await activate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task SingleConnectionRotation_RewritesTheRuntimeConfig()
    {
        var created = await _super.PostAsJsonAsync("/api/v1/users", new
        {
            name = "Solo",
            quota = (long?)null,
            expiry = (string?)null,
            single_connection = true,
            enabled = true,
        });
        var user = await created.Content.ReadFromJsonAsync<JsonElement>();
        var oldSecret = user.GetProperty("secret").GetString()!;

        var link = await _factory.CreateClient().GetStringAsync($"/api/v1/config/{user.GetProperty("id").GetString()}");

        var newSecret = link["vless://".Length..link.IndexOf('@')];
        Assert.NotEqual(oldSecret, newSecret);
        var runtimeConfig = await File.ReadAllTextAsync(Path.Combine(_factory.RuntimeDir, "config.json"));
        Assert.Contains(newSecret, runtimeConfig);
        Assert.DoesNotContain(oldSecret, runtimeConfig);
    }

    [Fact]
    public async Task InvalidDefaultProfile_ConfigLinksReturn503()
    {
        using var factory = new CustomWebApplicationFactory().WithProfiles(defaultProfile: TestProfiles.Broken);
        var super = await TestAuth.CreateSuperAdminClientAsync(factory);
        var created = await super.PostAsJsonAsync("/api/v1/users", new
        {
            name = "Alice",
            quota = (long?)null,
            expiry = (string?)null,
            single_connection = false,
            enabled = true,
        });
        var userId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var config = await factory.CreateClient().GetAsync($"/api/v1/config/{userId}");
        var state = await super.GetFromJsonAsync<JsonElement>("/api/v1/xray/profiles");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, config.StatusCode);
        Assert.Contains("is invalid", state.GetProperty("last_error").GetString());
    }
}
