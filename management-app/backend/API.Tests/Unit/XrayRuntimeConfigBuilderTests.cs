using System.Text.Json.Nodes;
using API.Models;
using API.Services.Xray;

namespace API.Tests.Unit;

public class XrayRuntimeConfigBuilderTests
{
    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json, documentOptions: XrayRuntimeConfigBuilder.ParseOptions)!;

    private static User EnabledUser(string secret) => new() { Id = "id-" + secret, Name = "Name " + secret, Secret = secret, Enabled = true };

    private static JsonObject Inbound(JsonObject config, string tag) =>
        config["inbounds"]!.AsArray().OfType<JsonObject>().Single(i => XrayRuntimeConfigBuilder.TagOf(i) == tag);

    [Fact]
    public void Build_AddsManagementSections()
    {
        var built = XrayRuntimeConfigBuilder.Build(Parse(TestProfiles.RealityConfig), [], apiPort: 10085);

        Assert.IsType<JsonObject>(built["stats"]);
        Assert.Equal("api", built["api"]!["tag"]!.GetValue<string>());
        Assert.Equal(new[] { "HandlerService", "StatsService" }, built["api"]!["services"]!.AsArray().Select(s => s!.GetValue<string>()).ToArray());
        var api = Inbound(built, "api");
        Assert.Equal("dokodemo-door", api["protocol"]!.GetValue<string>());
        Assert.Equal(10085, api["port"]!.GetValue<int>());
        var firstRule = built["routing"]!["rules"]![0]!;
        Assert.Equal("api", firstRule["outboundTag"]!.GetValue<string>());
        Assert.Equal("api", firstRule["inboundTag"]![0]!.GetValue<string>());
        Assert.True(built["policy"]!["levels"]!["0"]!["statsUserUplink"]!.GetValue<bool>());
        Assert.True(built["policy"]!["levels"]!["0"]!["statsUserDownlink"]!.GetValue<bool>());
    }

    [Fact]
    public void Build_ExistingManagementSections_AreNormalizedNotDuplicated()
    {
        var legacy = Parse("""
            {
              "stats": {},
              "api": { "tag": "api", "services": ["StatsService"] },
              "inbounds": [
                { "tag": "vless-in", "port": 443, "protocol": "vless", "settings": { "clients": [] } },
                { "tag": "api", "port": 10085, "protocol": "dokodemo-door", "settings": { "address": "127.0.0.1" } }
              ],
              "routing": { "rules": [ { "type": "field", "inboundTag": ["api"], "outboundTag": "api" } ] }
            }
            """);

        var built = XrayRuntimeConfigBuilder.Build(legacy, [], 10085);

        Assert.Equal(new[] { "StatsService", "HandlerService" }, built["api"]!["services"]!.AsArray().Select(s => s!.GetValue<string>()).ToArray());
        Assert.Single(built["inbounds"]!.AsArray(), i => XrayRuntimeConfigBuilder.TagOf((JsonObject)i!) == "api");
        Assert.Single(built["routing"]!["rules"]!.AsArray());
    }

    [Fact]
    public void Build_IsIdempotent()
    {
        var users = new[] { EnabledUser("s1") };
        var once = XrayRuntimeConfigBuilder.Build(Parse(TestProfiles.RealityConfig), users, 10085);
        var twice = XrayRuntimeConfigBuilder.Build(once, users, 10085);

        Assert.Equal(XrayRuntimeConfigBuilder.Serialize(once), XrayRuntimeConfigBuilder.Serialize(twice));
    }

    [Fact]
    public void Build_PreservesTheRestOfTheProfileAndDoesNotChangeTheInput()
    {
        var profile = Parse(TestProfiles.RealityConfig);
        var before = profile.ToJsonString();

        var built = XrayRuntimeConfigBuilder.Build(profile, [EnabledUser("s1")], 10085);

        Assert.Equal(before, profile.ToJsonString());
        Assert.Equal("warning", built["log"]!["loglevel"]!.GetValue<string>());
        Assert.Equal("direct", built["outbounds"]![0]!["tag"]!.GetValue<string>());
        Assert.Equal("test-private-key", Inbound(built, "vless-in")["streamSettings"]!["realitySettings"]!["privateKey"]!.GetValue<string>());
        // The profile's own rule stays, after the api rule.
        Assert.Equal("block", built["routing"]!["rules"]![1]!["outboundTag"]!.GetValue<string>());
    }

    [Fact]
    public void Build_WritesEnabledUsersWithTheSecretAsEmail()
    {
        var built = XrayRuntimeConfigBuilder.Build(Parse(TestProfiles.RealityConfig), [EnabledUser("s1"), EnabledUser("s2")], 10085);

        var clients = Inbound(built, "vless-in")["settings"]!["clients"]!.AsArray();
        Assert.Equal(2, clients.Count);
        Assert.All(clients, c => Assert.Equal(c!["id"]!.GetValue<string>(), c["email"]!.GetValue<string>()));
        Assert.All(clients, c => Assert.Equal("xtls-rprx-vision", c!["flow"]!.GetValue<string>()));
    }

    [Fact]
    public void Build_WebSocketInbound_ClientsHaveNoFlow()
    {
        var built = XrayRuntimeConfigBuilder.Build(Parse(TestProfiles.WebSocketConfig("/c.pem", "/k.pem")), [EnabledUser("s1")], 10085);

        var client = Inbound(built, "vless-cf")["settings"]!["clients"]![0]!.AsObject();
        Assert.False(client.ContainsKey("flow"));
    }

    [Fact]
    public void Build_AddsDecryptionNoneWhenMissing()
    {
        var built = XrayRuntimeConfigBuilder.Build(
            Parse("""{ "inbounds": [ { "tag": "v", "port": 443, "protocol": "vless" } ] }"""), [], 10085);

        Assert.Equal("none", Inbound(built, "v")["settings"]!["decryption"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("tcp", "reality", "xtls-rprx-vision")]
    [InlineData("raw", "tls", "xtls-rprx-vision")]
    [InlineData(null, "reality", "xtls-rprx-vision")]
    [InlineData("TCP", "REALITY", "xtls-rprx-vision")]
    [InlineData("tcp", "none", "")]
    [InlineData("tcp", null, "")]
    [InlineData("ws", "tls", "")]
    [InlineData("grpc", "reality", "")]
    [InlineData("xhttp", "tls", "")]
    public void DeriveFlow_VisionOnlyForRawTcpWithTlsOrReality(string? network, string? security, string expected)
    {
        var stream = new JsonObject();
        if (network is not null) stream["network"] = network;
        if (security is not null) stream["security"] = security;
        var inbound = new JsonObject { ["protocol"] = "vless", ["streamSettings"] = stream };

        Assert.Equal(expected, XrayRuntimeConfigBuilder.DeriveFlow(inbound));
    }

    [Fact]
    public void StructureHash_SameWhenOnlyUsersChange()
    {
        var profile = Parse(TestProfiles.RealityConfig);
        var withOne = XrayRuntimeConfigBuilder.Build(profile, [EnabledUser("s1")], 10085);
        var withTwo = XrayRuntimeConfigBuilder.Build(profile, [EnabledUser("s1"), EnabledUser("s2")], 10085);

        Assert.Equal(
            XrayRuntimeConfigBuilder.StructureHash(withOne, "p"),
            XrayRuntimeConfigBuilder.StructureHash(withTwo, "p"));
    }

    [Fact]
    public void StructureHash_ChangesWithTheProfileContentOrName()
    {
        var original = XrayRuntimeConfigBuilder.Build(Parse(TestProfiles.RealityConfig), [], 10085);
        var edited = XrayRuntimeConfigBuilder.Build(Parse(TestProfiles.RealityConfig.Replace("a1b2c3d4", "ffff0000")), [], 10085);

        var hash = XrayRuntimeConfigBuilder.StructureHash(original, "p");
        Assert.NotEqual(hash, XrayRuntimeConfigBuilder.StructureHash(edited, "p"));
        Assert.NotEqual(hash, XrayRuntimeConfigBuilder.StructureHash(original, "other"));
    }

    [Fact]
    public void InjectClients_OnlyTag_LeavesOtherInboundsAlone()
    {
        var config = Parse("""
            { "inbounds": [
                { "tag": "vless-in", "protocol": "vless", "settings": { "clients": [] } },
                { "tag": "other", "protocol": "vless", "settings": { "clients": [ { "id": "keep" } ] } } ] }
            """);

        XrayRuntimeConfigBuilder.InjectClients(config, [EnabledUser("s1")], onlyTag: "vless-in");

        Assert.Equal("s1", Inbound(config, "vless-in")["settings"]!["clients"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("keep", Inbound(config, "other")["settings"]!["clients"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void DescribeVlessInbounds_ListsTagsTransportAndFlow()
    {
        var inbounds = XrayRuntimeConfigBuilder.DescribeVlessInbounds(
            XrayRuntimeConfigBuilder.Build(Parse(TestProfiles.RealityConfig), [], 10085));

        var inbound = Assert.Single(inbounds);
        Assert.Equal(new XrayInboundInfo("vless-in", "tcp", "reality", "443", "xtls-rprx-vision"), inbound);
    }
}
