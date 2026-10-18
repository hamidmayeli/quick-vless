namespace API.Tests;

/// <summary>Profile folders for tests: a REALITY one, a WebSocket+TLS one, and a broken one.</summary>
internal static class TestProfiles
{
    public const string Reality = "vless-reality";
    public const string WebSocket = "vless-cf-ws";
    public const string Broken = "broken";

    public const string RealityConfig = """
        {
          "log": { "loglevel": "warning" },
          "inbounds": [
            {
              "tag": "vless-in",
              "listen": "0.0.0.0",
              "port": 443,
              "protocol": "vless",
              "settings": { "clients": [], "decryption": "none" },
              "streamSettings": {
                "network": "tcp",
                "security": "reality",
                "realitySettings": {
                  "dest": "www.microsoft.com:443",
                  "serverNames": ["www.microsoft.com"],
                  "privateKey": "test-private-key",
                  "shortIds": ["a1b2c3d4"]
                }
              }
            }
          ],
          "outbounds": [ { "protocol": "freedom", "tag": "direct" } ],
          "routing": { "rules": [ { "type": "field", "domain": ["geosite:category-ads-all"], "outboundTag": "block" } ] }
        }
        """;

    public const string RealityTemplate =
        "vless://{{secret}}@vpn.example.com:443?type=tcp&security=reality&pbk=pk&fp=chrome&sni=www.microsoft.com&sid=a1b2c3d4&spx=%2F&flow=xtls-rprx-vision#{{name}}";

    public const string WebSocketTemplate =
        "vless://{{secret}}@cdn.example.com:443?encryption=none&security=tls&sni=cdn.example.com&fp=chrome&alpn=http%2F1.1&type=ws&host=cdn.example.com&path=%2Fws-test#{{name}}";

    public static string WebSocketConfig(string certPath, string keyPath) => $$"""
        {
          "inbounds": [
            {
              "tag": "vless-cf",
              "listen": "0.0.0.0",
              "port": 443,
              "protocol": "vless",
              "settings": { "clients": [], "decryption": "none" },
              "streamSettings": {
                "network": "ws",
                "security": "tls",
                "tlsSettings": {
                  "alpn": ["http/1.1"],
                  "certificates": [ { "certificateFile": "{{certPath}}", "keyFile": "{{keyPath}}" } ]
                },
                "wsSettings": { "path": "/ws-test" }
              }
            }
          ],
          "outbounds": [ { "protocol": "freedom", "tag": "direct" } ]
        }
        """;

    /// <summary>Writes a profile folder and returns its path.</summary>
    public static string Write(string profilesDir, string name, string config, string? template)
    {
        var dir = Path.Combine(profilesDir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config.json"), config);
        if (template is not null) File.WriteAllText(Path.Combine(dir, "client-config.template"), template);
        return dir;
    }

    /// <summary>Writes the REALITY, WebSocket (with dummy cert files) and broken profiles.</summary>
    public static void WriteAll(string profilesDir)
    {
        Write(profilesDir, Reality, RealityConfig, RealityTemplate);

        var tlsDir = Path.Combine(profilesDir, WebSocket, "tls");
        Directory.CreateDirectory(tlsDir);
        // Forward slashes keep the paths valid inside JSON on every OS.
        var cert = Path.Combine(tlsDir, "fullchain.pem").Replace('\\', '/');
        var key = Path.Combine(tlsDir, "privkey.pem").Replace('\\', '/');
        File.WriteAllText(cert, "dummy cert");
        File.WriteAllText(key, "dummy key");
        Write(profilesDir, WebSocket, WebSocketConfig(cert, key), WebSocketTemplate);

        Write(profilesDir, Broken, RealityConfig.Replace("test-private-key", "YOUR_PRIVATE_KEY"), RealityTemplate);
    }
}
