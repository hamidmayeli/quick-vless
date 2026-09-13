using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace API.Tests.Integration;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "easy-xray-test-" + Guid.NewGuid());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-key-must-be-at-least-32-chars!!",
                ["Jwt:Issuer"] = "easy-xray-test",
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
