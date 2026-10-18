using API.Services.Xray;

namespace API.Tests.Unit;

public sealed class XrayProfileCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "easy-xray-profiles-" + Guid.NewGuid());

    public XrayProfileCatalogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private XrayProfileCatalog Catalog() => new(new XrayOptions { ProfilesDir = _dir });

    private XrayProfile Load(string config, string? template = TestProfiles.RealityTemplate)
    {
        TestProfiles.Write(_dir, "p", config, template);
        return Catalog().Find("p")!;
    }

    private static void AssertError(XrayProfile profile, string expected)
    {
        Assert.False(profile.IsValid);
        Assert.Contains(profile.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void List_ReturnsValidFoldersSortedAndIgnoresTheRest()
    {
        TestProfiles.Write(_dir, "b-profile", TestProfiles.RealityConfig, TestProfiles.RealityTemplate);
        TestProfiles.Write(_dir, "a-profile", TestProfiles.RealityConfig, TestProfiles.RealityTemplate);
        TestProfiles.Write(_dir, "Bad Name", TestProfiles.RealityConfig, TestProfiles.RealityTemplate);
        TestProfiles.Write(_dir, ".hidden", TestProfiles.RealityConfig, TestProfiles.RealityTemplate);
        Directory.CreateDirectory(Path.Combine(_dir, "no-config"));
        File.WriteAllText(Path.Combine(_dir, "readme.txt"), "not a profile");

        var names = Catalog().List().Select(p => p.Name).ToArray();

        Assert.Equal(new[] { "a-profile", "b-profile" }, names);
    }

    [Fact]
    public void List_WithoutProfilesDir_IsEmpty()
    {
        Assert.Empty(new XrayProfileCatalog(new XrayOptions()).List());
        Assert.Empty(new XrayProfileCatalog(new XrayOptions { ProfilesDir = Path.Combine(_dir, "missing") }).List());
    }

    [Fact]
    public void Find_OnlyMatchesListedNames()
    {
        TestProfiles.Write(_dir, "p", TestProfiles.RealityConfig, TestProfiles.RealityTemplate);

        Assert.NotNull(Catalog().Find("p"));
        Assert.Null(Catalog().Find("../p"));
        Assert.Null(Catalog().Find("P"));
    }

    [Fact]
    public void Load_ValidRealityProfile_HasNoErrorsAndDescribesItsInbound()
    {
        var profile = Load(TestProfiles.RealityConfig);

        Assert.True(profile.IsValid, string.Join(" | ", profile.Errors));
        Assert.Equal("xtls-rprx-vision", Assert.Single(profile.Inbounds).Flow);
    }

    [Fact]
    public void Load_ValidWebSocketProfileWithCertificates_HasNoErrors()
    {
        TestProfiles.WriteAll(_dir);

        var profile = Catalog().Find(TestProfiles.WebSocket)!;

        Assert.True(profile.IsValid, string.Join(" | ", profile.Errors));
        Assert.Equal("", Assert.Single(profile.Inbounds).Flow);
    }

    [Fact]
    public void Load_MissingTemplate()
    {
        AssertError(Load(TestProfiles.RealityConfig, template: null), "client-config.template is missing.");
    }

    [Fact]
    public void Load_InvalidJson_ReportsTheLineButNotTheContent()
    {
        var profile = Load("{\n \"inbounds\": [ secret-value-123");

        AssertError(profile, "config.json is not valid JSON (line 2).");
        Assert.DoesNotContain(profile.Errors, e => e.Contains("secret-value-123"));
    }

    [Fact]
    public void Load_CommentsAndTrailingCommas_AreAccepted()
    {
        var profile = Load(TestProfiles.RealityConfig.Replace("\"log\":", "// a comment\n\"log\":").Replace("\"direct\" }", "\"direct\", }"));

        Assert.True(profile.IsValid, string.Join(" | ", profile.Errors));
    }

    [Fact]
    public void Load_NoInbounds()
    {
        AssertError(Load("""{ "outbounds": [] }"""), "has no \"inbounds\" list.");
    }

    [Fact]
    public void Load_NoVlessInbound()
    {
        AssertError(Load("""{ "inbounds": [ { "tag": "t", "port": 443, "protocol": "trojan" } ] }"""), "has no vless inbound");
    }

    [Fact]
    public void Load_VlessInboundWithoutTag()
    {
        AssertError(Load("""{ "inbounds": [ { "port": 443, "protocol": "vless" } ] }"""), "Every vless inbound needs a \"tag\".");
    }

    [Fact]
    public void Load_ApiTagIsReserved()
    {
        AssertError(Load("""{ "inbounds": [ { "tag": "api", "port": 443, "protocol": "vless" } ] }"""), "reserved for the management API");
    }

    [Fact]
    public void Load_DuplicateTags()
    {
        AssertError(
            Load("""{ "inbounds": [ { "tag": "v", "port": 443, "protocol": "vless" }, { "tag": "v", "port": 80, "protocol": "vless" } ] }"""),
            "The inbound tag \"v\" is used more than once.");
    }

    [Fact]
    public void Load_NothingOnPort443()
    {
        AssertError(Load(TestProfiles.RealityConfig.Replace("\"port\": 443", "\"port\": 8443")), "No inbound listens on port 443.");
    }

    [Fact]
    public void Load_PortAsString_IsAccepted()
    {
        Assert.True(Load(TestProfiles.RealityConfig.Replace("\"port\": 443", "\"port\": \"443\"")).IsValid);
    }

    [Fact]
    public void Load_XhttpWithVlessEncryption_IsValidAndNeedsVisionInTheLink()
    {
        var tls = Path.Combine(_dir, "certs");
        Directory.CreateDirectory(tls);
        var cert = Path.Combine(tls, "fullchain.pem").Replace('\\', '/');
        var key = Path.Combine(tls, "privkey.pem").Replace('\\', '/');
        File.WriteAllText(cert, "c");
        File.WriteAllText(key, "k");

        var valid = Load(TestProfiles.XhttpConfig(cert, key), TestProfiles.XhttpTemplate);
        Assert.True(valid.IsValid, string.Join(" | ", valid.Errors));
        Assert.Equal(new XrayInboundInfo("vless-xhttp", "xhttp", "tls", "443", "xtls-rprx-vision"), Assert.Single(valid.Inbounds));

        AssertError(Load(TestProfiles.XhttpConfig(cert, key), TestProfiles.XhttpTemplate.Replace("&flow=xtls-rprx-vision", "")),
            "needs flow=xtls-rprx-vision");
    }

    [Theory]
    [InlineData("YOUR_CF_DOMAIN", "run install.sh with --cf-domain")]
    [InlineData("YOUR_WS_ID", "run install.sh with --cf-domain")]
    [InlineData("YOUR_XHTTP_ID", "run install.sh with --cf-domain")]
    [InlineData("YOUR_VLESS_ENC_PRIVATE_KEY", "run install.sh to generate the VLESS Encryption keys")]
    [InlineData("YOUR_PRIVATE_KEY", "run install.sh to fill in the REALITY settings")]
    [InlineData("YOUR_SOMETHING", "replace it by hand")]
    public void Load_UnfilledInstallPlaceholder_SaysHowToFillIt(string placeholder, string hint)
    {
        AssertError(Load(TestProfiles.RealityConfig.Replace("test-private-key", placeholder)), $"config.json still contains {placeholder}: {hint}.");
    }

    [Fact]
    public void Load_UnfilledPlaceholderInTemplate()
    {
        AssertError(
            Load(TestProfiles.RealityConfig, TestProfiles.RealityTemplate.Replace("vpn.example.com", "YOUR_HOST")),
            "client-config.template still contains YOUR_HOST");
    }

    [Theory]
    [InlineData("https://{{secret}}@h:443?flow=xtls-rprx-vision#{{name}}", "must start with vless://.")]
    [InlineData("vless://user@h:443?flow=xtls-rprx-vision#{{name}}", "must contain {{secret}}.")]
    [InlineData("vless://{{secret}}@{{host}}:443?flow=xtls-rprx-vision", "uses {{host}}. Only {{secret}} and {{name}} are supported.")]
    [InlineData("   \n  ", "client-config.template is empty.")]
    public void Load_MalformedTemplate(string template, string expected)
    {
        AssertError(Load(TestProfiles.RealityConfig, template), expected);
    }

    [Fact]
    public void Load_VisionFlowInWebSocketTemplate_IsRejected()
    {
        TestProfiles.WriteAll(_dir);
        File.WriteAllText(Path.Combine(_dir, TestProfiles.WebSocket, "client-config.template"),
            TestProfiles.WebSocketTemplate.Replace("type=ws", "type=ws&flow=xtls-rprx-vision"));

        AssertError(Catalog().Find(TestProfiles.WebSocket)!, "sets flow=xtls-rprx-vision, but no vless inbound supports it");
    }

    [Fact]
    public void Load_RealityTemplateWithoutVisionFlow_IsRejected()
    {
        AssertError(
            Load(TestProfiles.RealityConfig, TestProfiles.RealityTemplate.Replace("&flow=xtls-rprx-vision", "")),
            "needs flow=xtls-rprx-vision");
    }

    [Fact]
    public void Load_MissingCertificateFile()
    {
        AssertError(Load(TestProfiles.WebSocketConfig("/nowhere/fullchain.pem", "/nowhere/privkey.pem"), TestProfiles.WebSocketTemplate),
            "Certificate file /nowhere/fullchain.pem was not found or can't be read.");
    }
}
