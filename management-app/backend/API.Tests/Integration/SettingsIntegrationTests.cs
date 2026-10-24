using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration;

public sealed class SettingsIntegrationTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _super;

    public SettingsIntegrationTests()
    {
        _super = TestAuth.CreateSuperAdminClientAsync(_factory).GetAwaiter().GetResult();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task UpdateSettings_PersistsDomainOverrideAndReturnsItToAdmins()
    {
        var response = await _super.PutAsJsonAsync("/api/v1/settings", new { domain_override = "  vpn.example.com/  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("vpn.example.com", (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("domain_override").GetString());

        var admin = await TestAuth.CreateAdminClientAsync(_factory, _super, "alice");
        var settings = await admin.GetFromJsonAsync<JsonElement>("/api/v1/settings");
        Assert.Equal("vpn.example.com", settings.GetProperty("domain_override").GetString());
        Assert.Contains("\"domain_override\": \"vpn.example.com\"", await File.ReadAllTextAsync(Path.Combine(_factory.DataDir, "settings.json")));
    }

    [Fact]
    public async Task UpdateSettings_RejectsNonSuperAdminAndInvalidDomains()
    {
        var admin = await TestAuth.CreateAdminClientAsync(_factory, _super, "alice");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PutAsJsonAsync("/api/v1/settings", new { domain_override = "vpn.example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _super.PutAsJsonAsync("/api/v1/settings", new { domain_override = "https://vpn.example.com/path" })).StatusCode);
    }
}