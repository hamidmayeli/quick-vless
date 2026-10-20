using Microsoft.Extensions.Configuration;
using API.Services;

namespace API.Tests;

public class VlessUrlGeneratorTests
{
    // Legacy mode: the link is built from Xray:* settings, exactly as before profiles existed.
    private static LegacyVlessUrl CreateGenerator(string host = "example.com", string port = "443",
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
        return new LegacyVlessUrl(config);
    }

    [Fact]
    public void Generate_Legacy_MatchesTheLinkBuiltBeforeProfiles()
    {
        var url = CreateGenerator(host: "vpn.example.com", publicKey: "pk", shortId: "a1b2").Generate("uuid-1", "My User");

        Assert.Equal(
            "vless://uuid-1@vpn.example.com:443?type=tcp&security=reality&pbk=pk&fp=chrome&sni=www.microsoft.com&sid=a1b2&spx=%2F&flow=xtls-rprx-vision#My%20User",
            url);
    }

    [Fact]
    public void Render_FillsSecretAndEncodedName()
    {
        var url = VlessUrlGenerator.Render("vless://{{secret}}@host:443?type=ws#{{name}}", "uuid-1", "Ann & Bob");

        Assert.Equal("vless://uuid-1@host:443?type=ws#Ann%20%26%20Bob", url);
    }

    [Fact]
    public void Render_AllowsSpacesInsidePlaceholders()
    {
        Assert.Equal("vless://s@h#n", VlessUrlGenerator.Render("vless://{{ secret }}@h#{{ name }}", "s", "n"));
    }

    [Fact]
    public void Render_MultiLineTemplate_ReturnsOneLinkPerNonEmptyLine()
    {
        var links = VlessUrlGenerator.Render("vless://{{secret}}@a#{{name}}\r\n\n  vless://{{secret}}@b#{{name}}  \n", "s", "n");

        Assert.Equal("vless://s@a#n\nvless://s@b#n", links);
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
