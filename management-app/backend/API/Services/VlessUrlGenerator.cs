namespace API.Services;

public sealed class VlessUrlGenerator(IConfiguration config)
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
