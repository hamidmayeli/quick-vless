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
        Assert.Null((await CreateRepo().GetAsync()).XrayActiveProfile);
    }

    [Fact]
    public async Task Update_RoundTripsAsSnakeCaseWithoutLeavingATempFile()
    {
        await CreateRepo().UpdateAsync(s => s.XrayActiveProfile = "vless-cf-ws");

        Assert.Equal("vless-cf-ws", (await CreateRepo().GetAsync()).XrayActiveProfile);
        Assert.Contains("\"xray_active_profile\": \"vless-cf-ws\"", await File.ReadAllTextAsync(SettingsPath));
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName).ToArray());
    }
}
