using Microsoft.Extensions.Configuration;
using API.Services;

namespace API.Tests;

public class VlessUrlGeneratorTests
{
    private static VlessUrlGenerator CreateGenerator(string host = "example.com", string port = "443",
        string publicKey = "TEST_PK", string shortId = "abc123", string sni = "www.microsoft.com")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Xray:Host"] = host,
                ["Xray:Port"] = port,
                ["Xray:RealityPublicKey"] = publicKey,
                ["Xray:RealityShortId"] = shortId,
                ["Xray:Sni"] = sni,
                ["Xray:Fingerprint"] = "chrome",
            })
            .Build();
        return new VlessUrlGenerator(config);
    }

    [Fact]
    public void Generate_StartsWithVlessScheme()
    {
        var gen = CreateGenerator();
        var url = gen.Generate("test-secret", "Alice");
        Assert.StartsWith("vless://", url);
    }

    [Fact]
    public void Generate_ContainsSecret()
    {
        var gen = CreateGenerator();
        const string secret = "my-secret-uuid";
        var url = gen.Generate(secret, "Alice");
        Assert.Contains(secret, url);
    }

    [Fact]
    public void Generate_ContainsHostAndPort()
    {
        var gen = CreateGenerator(host: "vpn.example.com", port: "8443");
        var url = gen.Generate("sec", "Bob");
        Assert.Contains("vpn.example.com:8443", url);
    }

    [Fact]
    public void Generate_ContainsRealityParams()
    {
        var gen = CreateGenerator(publicKey: "pubkey123", shortId: "sid99");
        var url = gen.Generate("sec", "Charlie");
        Assert.Contains("security=reality", url);
        Assert.Contains("pbk=", url);
        Assert.Contains("sid=", url);
    }

    [Fact]
    public void Generate_ContainsVisionFlow()
    {
        var gen = CreateGenerator();
        var url = gen.Generate("sec", "Dave");
        Assert.Contains("flow=xtls-rprx-vision", url);
    }

    [Fact]
    public void Generate_EndsWithEncodedName()
    {
        var gen = CreateGenerator();
        var url = gen.Generate("sec", "My User");
        Assert.Contains("My%20User", url);
    }
}
