using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests.Integration;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    public const string JwtSecret = "test-secret-key-must-be-at-least-32-chars!!";
    public const string JwtIssuer = "easy-xray-test";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "easy-xray-test-" + Guid.NewGuid());

    public string DataDir => _tempDir;
    public string ProfilesDir => Path.Combine(_tempDir, "profiles");
    public string RuntimeDir => Path.Combine(_tempDir, "xray-runtime");

    /// <summary>Extra configuration, applied after the defaults. Set before the first CreateClient().</summary>
    public Dictionary<string, string?> Settings { get; } = [];

    /// <summary>Service replacements (e.g. fakes). Set before the first CreateClient().</summary>
    public Action<IServiceCollection>? TestServices { get; set; }

    /// <summary>Writes a file into the data directory. Call before the first CreateClient() so startup sees it.</summary>
    public void SeedFile(string relativePath, string content)
    {
        var path = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>Switches the app to profile mode with the test profiles (REALITY, WebSocket, broken).</summary>
    public CustomWebApplicationFactory WithProfiles(string? defaultProfile = null)
    {
        TestProfiles.WriteAll(ProfilesDir);
        Settings["Xray:ProfilesDir"] = ProfilesDir;
        if (defaultProfile is not null) Settings["Xray:DefaultProfile"] = defaultProfile;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = JwtSecret,
                ["Jwt:Issuer"] = JwtIssuer,
                ["Storage:UsersPath"] = Path.Combine(_tempDir, "users.json"),
                ["Storage:AdminsPath"] = Path.Combine(_tempDir, "admins.json"),
                ["Storage:UsageHistoryDir"] = Path.Combine(_tempDir, "usage_history"),
                ["Storage:SettingsPath"] = Path.Combine(_tempDir, "settings.json"),
                ["Xray:RuntimeDir"] = RuntimeDir,
                ["Xray:RestartConfirmTimeoutSeconds"] = "0",
                ["Xray:BinaryPath"] = "nonexistent-xray-binary",
                ["Xray:ConfigPath"] = Path.Combine(_tempDir, "nonexistent-config.json"),
                ["Xray:StatsServer"] = "127.0.0.1:10085",
                ["Xray:Host"] = "test.example.com",
                ["Xray:Port"] = "443",
                ["Xray:RealityPublicKey"] = "test-pubkey",
                ["Xray:RealityShortId"] = "testid",
                ["Xray:Sni"] = "test.example.com",
                ["Xray:Fingerprint"] = "chrome",
            });
            cfg.AddInMemoryCollection(Settings);
        });
        if (TestServices is not null) builder.ConfigureTestServices(TestServices);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
