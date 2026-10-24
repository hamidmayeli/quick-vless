using Microsoft.Extensions.Configuration;
using API.Storage;

namespace API.Tests.Unit;

public sealed class SettingsRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "easy-xray-settings-" + Guid.NewGuid());

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private SettingsRepository CreateRepo() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:SettingsPath"] = SettingsPath })
            .Build());

    [Fact]
    public async Task Get_NoFile_ReturnsDefaults()
    {
        var settings = await CreateRepo().GetAsync();
        Assert.Null(settings.XrayActiveProfile);
        Assert.Null(settings.CustomDomain);
    }

    [Fact]
    public async Task Update_RoundTripsAsSnakeCaseWithoutLeavingATempFile()
    {
        await CreateRepo().UpdateAsync(s => s.XrayActiveProfile = "vless-cf-ws");
        await CreateRepo().UpdateAsync(s => s.CustomDomain = "vpn.example.com");

        var settings = await CreateRepo().GetAsync();
        Assert.Equal("vless-cf-ws", settings.XrayActiveProfile);
        Assert.Equal("vpn.example.com", settings.CustomDomain);
        var json = await File.ReadAllTextAsync(SettingsPath);
        Assert.Contains("\"xray_active_profile\": \"vless-cf-ws\"", json);
        Assert.Contains("\"custom_domain\": \"vpn.example.com\"", json);
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName).ToArray());
    }
}
