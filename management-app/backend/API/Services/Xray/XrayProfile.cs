using System.Text.Json.Nodes;

namespace API.Services.Xray;

/// <summary>A folder holding an Xray config.json and the matching client-config.template.</summary>
public sealed record XrayProfile(
    string Name,
    IReadOnlyList<XrayInboundInfo> Inbounds,
    IReadOnlyList<string> Errors,
    JsonObject? Config,
    string? Template)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>A VLESS inbound users are added to, and the flow they need on it.</summary>
public sealed record XrayInboundInfo(string Tag, string Network, string Security, string? Port, string Flow);
