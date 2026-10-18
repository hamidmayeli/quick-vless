using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace API.Services.Xray;

/// <summary>Lists and validates the profiles in <see cref="XrayOptions.ProfilesDir"/>. Reads the disk on every call.</summary>
public sealed partial class XrayProfileCatalog(XrayOptions options)
{
    public const string ConfigFileName = "config.json";
    public const string TemplateFileName = "client-config.template";

    private static readonly HashSet<string> TemplatePlaceholders = ["secret", "name"];

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex ProfileName();

    [GeneratedRegex(@"YOUR_[A-Z0-9_]+")]
    private static partial Regex InstallPlaceholder();

    [GeneratedRegex(@"\{\{\s*([^}]*?)\s*\}\}")]
    private static partial Regex TemplatePlaceholder();

    [GeneratedRegex(@"[?&]flow=xtls-rprx-vision(?=[&#]|$)")]
    private static partial Regex VisionFlowParam();

    public IReadOnlyList<XrayProfile> List()
    {
        if (!options.IsProfileMode) return [];
        return Directory.GetDirectories(options.ProfilesDir!)
            .Select(dir => (Dir: dir, Name: Path.GetFileName(dir)))
            .Where(p => ProfileName().IsMatch(p.Name) && File.Exists(Path.Combine(p.Dir, ConfigFileName)))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => Load(p.Name, p.Dir))
            .ToList();
    }

    // Looked up among the listed folders only, so a name can never be used as a path.
    public XrayProfile? Find(string name) => List().FirstOrDefault(p => p.Name == name);

    public static XrayProfile Load(string name, string directory)
    {
        var errors = new List<string>();
        var configText = File.ReadAllText(Path.Combine(directory, ConfigFileName));
        var templatePath = Path.Combine(directory, TemplateFileName);
        var template = File.Exists(templatePath) ? File.ReadAllText(templatePath) : null;
        if (template is null) errors.Add($"{TemplateFileName} is missing.");

        errors.AddRange(UnfilledInstallPlaceholders(ConfigFileName, configText));
        if (template is not null) errors.AddRange(UnfilledInstallPlaceholders(TemplateFileName, template));

        var config = ParseConfig(configText, errors);
        var inbounds = config is null ? [] : XrayRuntimeConfigBuilder.DescribeVlessInbounds(config);
        if (config is not null) ValidateConfig(config, errors);
        if (config is not null && template is not null) ValidateTemplate(template, inbounds, errors);

        return new XrayProfile(name, inbounds, errors, config, template);
    }

    private static JsonObject? ParseConfig(string text, List<string> errors)
    {
        try
        {
            if (JsonNode.Parse(text, documentOptions: XrayRuntimeConfigBuilder.ParseOptions) is JsonObject config)
                return config;
            errors.Add($"{ConfigFileName} must contain a JSON object.");
        }
        catch (JsonException ex)
        {
            // Only the position: the message could quote the file, which may contain private keys.
            errors.Add($"{ConfigFileName} is not valid JSON (line {(ex.LineNumber ?? 0) + 1}).");
        }
        return null;
    }

    private static void ValidateConfig(JsonObject config, List<string> errors)
    {
        if (config["inbounds"] is not JsonArray inbounds)
        {
            errors.Add($"{ConfigFileName} has no \"inbounds\" list.");
            return;
        }

        var all = inbounds.OfType<JsonObject>().ToList();
        var vless = all
            .Where(i => string.Equals(XrayRuntimeConfigBuilder.AsString(i["protocol"]), "vless", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (vless.Count == 0)
            errors.Add($"{ConfigFileName} has no vless inbound. The management app only manages VLESS users.");
        if (vless.Any(i => string.IsNullOrWhiteSpace(XrayRuntimeConfigBuilder.TagOf(i))))
            errors.Add("Every vless inbound needs a \"tag\".");
        if (vless.Any(i => XrayRuntimeConfigBuilder.TagOf(i) == XrayRuntimeConfigBuilder.ApiTag))
            errors.Add("The tag \"api\" is reserved for the management API.");

        foreach (var duplicate in all
                     .Select(XrayRuntimeConfigBuilder.TagOf)
                     .Where(t => !string.IsNullOrEmpty(t))
                     .GroupBy(t => t)
                     .Where(g => g.Count() > 1))
        {
            errors.Add($"The inbound tag \"{duplicate.Key}\" is used more than once.");
        }

        if (!all.Any(i => i["port"]?.ToJsonString().Trim('"') == "443"))
            errors.Add("No inbound listens on port 443. Caddy forwards VPN traffic to xray:443.");

        foreach (var inbound in all)
        {
            var tls = (inbound["streamSettings"] as JsonObject)?["tlsSettings"] as JsonObject;
            var certificates = tls?["certificates"] as JsonArray;
            foreach (var path in (certificates ?? [])
                         .OfType<JsonObject>()
                         .SelectMany(c => new[] { c["certificateFile"], c["keyFile"] })
                         .Select(XrayRuntimeConfigBuilder.AsString)
                         .OfType<string>())
            {
                if (!CanRead(path))
                    errors.Add($"Certificate file {path} was not found or can't be read.");
            }
        }
    }

    private static void ValidateTemplate(string template, IReadOnlyList<XrayInboundInfo> inbounds, List<string> errors)
    {
        var lines = TemplateLines(template).ToList();
        if (lines.Count == 0)
        {
            errors.Add($"{TemplateFileName} is empty.");
            return;
        }

        var anyVision = inbounds.Any(i => i.Flow == XrayRuntimeConfigBuilder.VisionFlow);
        var allVision = inbounds.Count > 0 && inbounds.All(i => i.Flow == XrayRuntimeConfigBuilder.VisionFlow);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var where = $"Line {i + 1} of {TemplateFileName}";
            if (!line.StartsWith("vless://", StringComparison.Ordinal))
                errors.Add($"{where} must start with vless://.");
            if (!TemplatePlaceholder().Matches(line).Any(m => m.Groups[1].Value == "secret"))
                errors.Add($"{where} must contain {{{{secret}}}}.");
            foreach (var unknown in TemplatePlaceholder().Matches(line)
                         .Select(m => m.Groups[1].Value)
                         .Where(p => !TemplatePlaceholders.Contains(p))
                         .Distinct())
            {
                errors.Add($"{where} uses {{{{{unknown}}}}}. Only {{{{secret}}}} and {{{{name}}}} are supported.");
            }

            var hasVision = VisionFlowParam().IsMatch(line);
            if (hasVision && !anyVision)
                errors.Add($"{where} sets flow=xtls-rprx-vision, but no vless inbound supports it. Vision needs tcp/raw with tls or reality.");
            if (!hasVision && allVision)
                errors.Add($"{where} needs flow=xtls-rprx-vision: the server requires it on this inbound.");
        }
    }

    public static IEnumerable<string> TemplateLines(string template) =>
        template.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    private static IEnumerable<string> UnfilledInstallPlaceholders(string file, string text) =>
        InstallPlaceholder().Matches(text)
            .Select(m => m.Value)
            .Distinct()
            .Select(p => $"{file} still contains {p}: {InstallHint(p)}.");

    private static string InstallHint(string placeholder) => placeholder switch
    {
        "YOUR_CF_DOMAIN" or "YOUR_WS_ID" or "YOUR_XHTTP_ID" => "run install.sh with --cf-domain",
        "YOUR_PRIVATE_KEY" or "YOUR_PUBLIC_KEY" or "YOUR_SHORT_ID" or "YOUR_SNI" or "YOUR_HOST" or "YOUR_FINGERPRINT"
            => "run install.sh to fill in the REALITY settings",
        "YOUR_VLESS_ENC_PRIVATE_KEY" or "YOUR_VLESS_ENC_PUBLIC_KEY" => "run install.sh to generate the VLESS Encryption keys",
        _ => "replace it by hand",
    };

    private static bool CanRead(string path)
    {
        try
        {
            using var _ = File.OpenRead(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
