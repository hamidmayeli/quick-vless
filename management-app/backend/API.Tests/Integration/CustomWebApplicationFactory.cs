using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace API.Tests.Integration;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    public const string JwtSecret = "test-secret-key-must-be-at-least-32-chars!!";
    public const string JwtIssuer = "easy-xray-test";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "easy-xray-test-" + Guid.NewGuid());

    public string DataDir => _tempDir;

    /// <summary>Writes a file into the data directory. Call before the first CreateClient() so startup sees it.</summary>
    public void SeedFile(string relativePath, string content)
    {
        var path = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
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
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
