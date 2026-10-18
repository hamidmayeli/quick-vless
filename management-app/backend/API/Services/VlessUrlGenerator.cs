using System.Text.RegularExpressions;
using API.Services.Xray;

namespace API.Services;

/// <summary>Builds the client link(s) for a user from the applied profile's client-config.template.</summary>
public sealed partial class VlessUrlGenerator(IConfiguration config, XrayRuntime runtime)
{
    private readonly LegacyVlessUrl _legacy = new(config);

    [GeneratedRegex(@"\{\{\s*(secret|name)\s*\}\}")]
    private static partial Regex Placeholder();

    /// <summary>False when the app is in profile mode but no valid profile has been applied yet.</summary>
    public bool CanGenerate => !runtime.IsProfileMode || runtime.ClientTemplate is not null;

    public string? Generate(string secret, string name)
    {
        if (!runtime.IsProfileMode) return _legacy.Generate(secret, name);
        return runtime.ClientTemplate is { } template ? Render(template, secret, name) : null;
    }

    /// <summary>Fills {{secret}} and {{name}} (URL-encoded); one link per non-empty line.</summary>
    public static string Render(string template, string secret, string name) =>
        string.Join("\n", XrayProfileCatalog.TemplateLines(template).Select(line =>
            Placeholder().Replace(line, m => m.Groups[1].Value == "secret" ? secret : Uri.EscapeDataString(name))));
}

/// <summary>The REALITY link built from Xray:* settings, used before profiles existed (legacy mode).</summary>
public sealed class LegacyVlessUrl(IConfiguration config)
{
    private readonly string _host = config["Xray:Host"] ?? "localhost";
    private readonly string _port = config["Xray:Port"] ?? "443";
    private readonly string _publicKey = config["Xray:RealityPublicKey"] ?? string.Empty;
    private readonly string _shortId = config["Xray:RealityShortId"] ?? string.Empty;
    private readonly string _sni = config["Xray:Sni"] ?? "www.microsoft.com";
    private readonly string _fingerprint = config["Xray:Fingerprint"] ?? "chrome";

    public string Generate(string secret, string name)
    {
        var query = $"type=tcp&security=reality&pbk={Uri.EscapeDataString(_publicKey)}" +
                    $"&fp={_fingerprint}&sni={Uri.EscapeDataString(_sni)}" +
                    $"&sid={Uri.EscapeDataString(_shortId)}&spx=%2F&flow=xtls-rprx-vision";
        return $"vless://{secret}@{_host}:{_port}?{query}#{Uri.EscapeDataString(name)}";
    }
}
