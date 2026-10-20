using Microsoft.Extensions.Logging.Abstractions;
using API.Services.Xray;

namespace API.Tests.Unit;

public class XrayBinaryConfigTesterTests
{
    [Fact]
    public void Summarize_PicksTheFailedToStartLine()
    {
        // Real output of `xray run -test` for a profile whose certificate is missing.
        const string output = """
            Xray 26.10.17 (Xray, Penetrates Everything.) 92ccc93 (go1.27.2 linux/amd64)
            A unified platform for anti-censorship.
            2026/10/19 04:47:11.050737 [Info] infra/conf/serial: Reading config: &{Name:/t/cf.json Format:json}
            Failed to start: main: failed to load config files: [/t/cf.json] > infra/conf: Failed to build TLS config. > open /etc/xray-cf/tls/fullchain.pem: no such file or directory
            """;

        Assert.StartsWith("Failed to start: main: failed to load config files", XrayBinaryConfigTester.Summarize(output));
    }

    [Fact]
    public void Summarize_WithoutAFailedLine_UsesTheLastLine()
    {
        Assert.Equal("something odd", XrayBinaryConfigTester.Summarize("first\nsomething odd\n"));
        Assert.Equal("xray rejected the config.", XrayBinaryConfigTester.Summarize(""));
    }

    [Fact]
    public async Task Test_WithoutTheBinary_IsSkippedAndPasses()
    {
        var tester = new XrayBinaryConfigTester(
            new XrayOptions { BinaryPath = "definitely-not-an-xray-binary-" + Guid.NewGuid() },
            NullLogger<XrayBinaryConfigTester>.Instance);

        var result = await tester.TestAsync("config.json");

        Assert.True(result.Passed);
    }
}
