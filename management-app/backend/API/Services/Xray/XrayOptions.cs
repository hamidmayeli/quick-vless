namespace API.Services.Xray;

/// <summary>Xray-related settings, read once from configuration.</summary>
public sealed record XrayOptions
{
    /// <summary>Folder holding one sub-folder per profile. When unset or missing the app runs in legacy mode.</summary>
    public string? ProfilesDir { get; init; }

    /// <summary>Folder the app writes the config Xray runs into. Shared with the Xray container.</summary>
    public string RuntimeDir { get; init; } = "/etc/xray/runtime";

    public string DefaultProfile { get; init; } = "vless-reality";
    public TimeSpan RestartConfirmTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public string BinaryPath { get; init; } = "xray";
    public int ApiPort { get; init; } = 10085;

    // Legacy mode: a single config file patched in place, as before profiles existed.
    public string LegacyConfigPath { get; init; } = "/usr/local/etc/xray/config.json";
    public string LegacyInboundTag { get; init; } = "vless-in";

    public bool IsProfileMode => !string.IsNullOrWhiteSpace(ProfilesDir) && Directory.Exists(ProfilesDir);

    public string RuntimeConfigPath => Path.Combine(RuntimeDir, "config.json");
    // Must end in .json: xray picks the config format from the file extension.
    public string CandidateConfigPath => Path.Combine(RuntimeDir, "config.next.json");
    public string RuntimeTemplatePath => Path.Combine(RuntimeDir, XrayProfileCatalog.TemplateFileName);
    public string StructureHashPath => Path.Combine(RuntimeDir, "structure.sha256");
    public string RunningHashPath => Path.Combine(RuntimeDir, "running.sha256");

    public static XrayOptions From(IConfiguration config)
    {
        var statsServer = config["Xray:StatsServer"] ?? "127.0.0.1:10085";
        var portText = statsServer[(statsServer.LastIndexOf(':') + 1)..];
        return new XrayOptions
        {
            ProfilesDir = config["Xray:ProfilesDir"],
            RuntimeDir = config["Xray:RuntimeDir"] ?? "/etc/xray/runtime",
            DefaultProfile = config["Xray:DefaultProfile"] ?? "vless-reality",
            RestartConfirmTimeout = TimeSpan.FromSeconds(
                int.TryParse(config["Xray:RestartConfirmTimeoutSeconds"], out var seconds) ? seconds : 30),
            BinaryPath = config["Xray:BinaryPath"] ?? "xray",
            ApiPort = int.TryParse(portText, out var port) ? port : 10085,
            LegacyConfigPath = config["Xray:ConfigPath"] ?? "/usr/local/etc/xray/config.json",
            LegacyInboundTag = config["Xray:InboundTag"] ?? "vless-in",
        };
    }
}
