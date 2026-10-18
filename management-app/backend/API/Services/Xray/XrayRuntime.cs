using System.Text.Json.Nodes;
using API.Models;
using API.Storage;

namespace API.Services.Xray;

public enum ActivationStatus { Activated, NotFound, Invalid, LegacyMode, Busy, RolledBack }

public sealed record ActivationResult(
    ActivationStatus Status,
    string? Active = null,
    IReadOnlyList<string>? Errors = null,
    string? Message = null);

/// <summary>
/// Owns the config Xray runs and every change to it. Profile switches and user changes share one lock,
/// so a user change is applied either entirely before or entirely after a switch.
/// </summary>
public sealed class XrayRuntime(
    XrayOptions options,
    XrayProfileCatalog catalog,
    UserRepository users,
    SettingsRepository settings,
    IXrayClient xray,
    IXrayConfigTester tester,
    IXrayRestartMonitor restartMonitor,
    ILogger<XrayRuntime> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _activating;

    // What the applied config contains: the VLESS inbounds users go into, and the matching client template.
    private IReadOnlyList<XrayInboundInfo> _inbounds = [];
    private string? _template;
    private string? _selectedProfile;
    private string? _lastError;

    public bool IsProfileMode => options.IsProfileMode;
    public string? SelectedProfile => _selectedProfile;
    public string? LastError => _lastError;

    /// <summary>Template of the applied profile; null in legacy mode or when nothing valid has been applied.</summary>
    public string? ClientTemplate => _template;

    /// <summary>Whether Xray runs the generated config. Null when that can't be known (legacy mode, no launcher).</summary>
    public bool? IsApplied
    {
        get
        {
            if (!options.IsProfileMode || !restartMonitor.IsEnabled) return null;
            var wanted = ReadTrimmed(options.StructureHashPath);
            return wanted is not null && wanted == ReadTrimmed(options.RunningHashPath);
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (options.IsProfileMode) await InitializeProfileModeLocked(ct);
            else await InitializeLegacyModeLocked();

            // Xray may have started before this app; make sure it knows every enabled user.
            foreach (var user in (await users.GetAllAsync()).Where(u => u.Enabled))
                await AddToXrayAsync(user);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ActivationResult> ActivateAsync(string name, CancellationToken ct = default)
    {
        if (!options.IsProfileMode)
            return new ActivationResult(ActivationStatus.LegacyMode, Message: "Profiles are not enabled. Run install.sh --upgrade to enable them.");
        if (Interlocked.CompareExchange(ref _activating, 1, 0) != 0)
            return new ActivationResult(ActivationStatus.Busy, Message: "Another profile switch is in progress.");
        try
        {
            await _lock.WaitAsync(ct);
            try
            {
                return await ActivateLocked(name, ct);
            }
            finally
            {
                _lock.Release();
            }
        }
        finally
        {
            Volatile.Write(ref _activating, 0);
        }
    }

    public Task UserAddedAsync(User user) =>
        ChangeUsersAsync(() => user.Enabled ? AddToXrayAsync(user) : Task.CompletedTask);

    public Task UserRemovedAsync(User user) =>
        ChangeUsersAsync(() => user.Enabled ? RemoveFromXrayAsync(user.Id, user.Secret) : Task.CompletedTask);

    public Task UserUpdatedAsync(User user, bool wasEnabled) =>
        ChangeUsersAsync(() => (wasEnabled, user.Enabled) switch
        {
            (false, true) => AddToXrayAsync(user),
            (true, false) => RemoveFromXrayAsync(user.Id, user.Secret),
            _ => Task.CompletedTask,
        });

    public Task SecretRotatedAsync(User user, string oldSecret) =>
        ChangeUsersAsync(async () =>
        {
            await RemoveFromXrayAsync(user.Id, oldSecret);
            await AddToXrayAsync(user);
        });

    private async Task InitializeProfileModeLocked(CancellationToken ct)
    {
        LoadAppliedSnapshot();
        var saved = (await settings.GetAsync()).XrayActiveProfile;
        _selectedProfile = saved ?? options.DefaultProfile;

        var profile = catalog.Find(_selectedProfile);
        if (profile is null)
        {
            SetError($"The active profile \"{_selectedProfile}\" was not found in {options.ProfilesDir}.");
            return;
        }
        if (!profile.IsValid)
        {
            SetError($"The active profile \"{profile.Name}\" is invalid: {string.Join(" ", profile.Errors)}");
            return;
        }

        // Only re-test when the structure changed (e.g. install.sh patched the profile); otherwise startup stays fast.
        var outcome = await ApplyLocked(profile, testEvenIfUnchanged: false, ct);
        if (outcome.Error is not null)
        {
            SetError($"The active profile \"{profile.Name}\" was not applied: {outcome.Error}");
            return;
        }
        _lastError = null;
        if (saved is null) await settings.UpdateAsync(s => s.XrayActiveProfile = profile.Name);
        logger.LogInformation("Xray profile {Profile} applied (structure changed: {Changed})", profile.Name, outcome.StructureChanged);
    }

    private async Task InitializeLegacyModeLocked()
    {
        var flow = XrayRuntimeConfigBuilder.VisionFlow;
        if (TryReadConfig(options.LegacyConfigPath) is { } legacy)
        {
            var inbound = XrayRuntimeConfigBuilder.VlessInbounds(legacy)
                .FirstOrDefault(i => XrayRuntimeConfigBuilder.TagOf(i) == options.LegacyInboundTag);
            if (inbound is not null) flow = XrayRuntimeConfigBuilder.DeriveFlow(inbound);
        }
        _inbounds = [new XrayInboundInfo(options.LegacyInboundTag, "tcp", "reality", null, flow)];
        await RewriteClientsLocked();
    }

    private async Task<ActivationResult> ActivateLocked(string name, CancellationToken ct)
    {
        var profile = catalog.Find(name);
        if (profile is null) return new ActivationResult(ActivationStatus.NotFound);
        if (!profile.IsValid) return new ActivationResult(ActivationStatus.Invalid, Errors: profile.Errors);

        var previous = RuntimeSnapshot.Capture(options);
        var (previousInbounds, previousTemplate, previousProfile) = (_inbounds, _template, _selectedProfile);

        var outcome = await ApplyLocked(profile, testEvenIfUnchanged: true, ct);
        if (outcome.Error is not null) return new ActivationResult(ActivationStatus.Invalid, Errors: [outcome.Error]);

        if (await restartMonitor.WaitForAsync(outcome.Hash, ct))
        {
            _selectedProfile = profile.Name;
            _lastError = null;
            await settings.UpdateAsync(s => s.XrayActiveProfile = profile.Name);
            logger.LogInformation("Xray profile {Profile} activated", profile.Name);
            return new ActivationResult(ActivationStatus.Activated, Active: profile.Name);
        }

        // Xray didn't come up with the new config: put the previous one back.
        var seconds = (int)options.RestartConfirmTimeout.TotalSeconds;
        var failure = $"Xray did not confirm a restart with \"{profile.Name}\" within {seconds} seconds.";
        string message;
        if (previous.StructureHash is null)
        {
            message = $"{failure} There was no previous config to roll back to.";
        }
        else
        {
            await previous.RestoreAsync(options);
            (_inbounds, _template) = (previousInbounds, previousTemplate);
            var restored = await restartMonitor.WaitForAsync(previous.StructureHash, ct);
            var target = previousProfile is null ? "the previous config" : $"\"{previousProfile}\"";
            message = restored
                ? $"{failure} Rolled back to {target}."
                : $"{failure} Rolled back to {target}, but Xray has not confirmed that restart either. Check `docker compose logs xray`.";
        }
        SetError(message);
        return new ActivationResult(ActivationStatus.RolledBack, Message: message);
    }

    private sealed record ApplyOutcome(string? Error, string Hash, bool StructureChanged);

    private async Task<ApplyOutcome> ApplyLocked(XrayProfile profile, bool testEvenIfUnchanged, CancellationToken ct)
    {
        var enabled = (await users.GetAllAsync()).Where(u => u.Enabled);
        var built = XrayRuntimeConfigBuilder.Build(profile.Config!, enabled, options.ApiPort);
        var hash = XrayRuntimeConfigBuilder.StructureHash(built, profile.Name);
        var structureChanged = ReadTrimmed(options.StructureHashPath) != hash;
        var json = XrayRuntimeConfigBuilder.Serialize(built);

        Directory.CreateDirectory(options.RuntimeDir);
        if (structureChanged || testEvenIfUnchanged)
        {
            var candidate = options.CandidateConfigPath;
            await File.WriteAllTextAsync(candidate, json, ct);
            var test = await tester.TestAsync(candidate, ct);
            if (!test.Passed)
            {
                File.Delete(candidate);
                return new ApplyOutcome($"Xray rejected the generated config: {test.Error}", hash, structureChanged);
            }
            File.Move(candidate, options.RuntimeConfigPath, overwrite: true);
        }
        else
        {
            await WriteAtomicAsync(options.RuntimeConfigPath, json);
        }

        await WriteAtomicAsync(options.RuntimeTemplatePath, profile.Template!);
        // Written last: xray-run.sh restarts Xray when this changes, so the config must already be in place.
        if (structureChanged) await WriteAtomicAsync(options.StructureHashPath, hash);

        _inbounds = XrayRuntimeConfigBuilder.DescribeVlessInbounds(built);
        _template = profile.Template;
        return new ApplyOutcome(null, hash, structureChanged);
    }

    private async Task ChangeUsersAsync(Func<Task> applyToXray)
    {
        await _lock.WaitAsync();
        try
        {
            // The file first, so a restarting Xray still picks the change up.
            await RewriteClientsLocked();
            await applyToXray();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Rewrites the clients of the applied config from users.json. The structure stays the same, so Xray isn't restarted.</summary>
    private async Task RewriteClientsLocked()
    {
        var path = options.IsProfileMode ? options.RuntimeConfigPath : options.LegacyConfigPath;
        if (TryReadConfig(path) is not { } config)
        {
            logger.LogWarning("Xray config not found at {Path}. Skipping sync.", path);
            return;
        }

        var enabled = (await users.GetAllAsync()).Where(u => u.Enabled).ToList();
        XrayRuntimeConfigBuilder.InjectClients(config, enabled, options.IsProfileMode ? null : options.LegacyInboundTag);
        await WriteAtomicAsync(path, XrayRuntimeConfigBuilder.Serialize(config));
        logger.LogInformation("Xray config synced with {Count} enabled users.", enabled.Count);
    }

    private async Task AddToXrayAsync(User user)
    {
        foreach (var inbound in _inbounds)
            await xray.AddUserAsync(user, inbound.Tag, inbound.Flow);
    }

    private async Task RemoveFromXrayAsync(string userId, string secret)
    {
        foreach (var inbound in _inbounds)
            await xray.RemoveUserAsync(userId, secret, inbound.Tag);
    }

    private void LoadAppliedSnapshot()
    {
        if (TryReadConfig(options.RuntimeConfigPath) is { } applied)
            _inbounds = XrayRuntimeConfigBuilder.DescribeVlessInbounds(applied);
        _template = File.Exists(options.RuntimeTemplatePath) ? File.ReadAllText(options.RuntimeTemplatePath) : null;
    }

    private void SetError(string message)
    {
        _lastError = message;
        logger.LogError("{Message}", message);
    }

    private static JsonObject? TryReadConfig(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: XrayRuntimeConfigBuilder.ParseOptions) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    internal static string? ReadTrimmed(string path) =>
        File.Exists(path) ? File.ReadAllText(path).Trim() : null;

    private static async Task WriteAtomicAsync(string path, string content)
    {
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, content);
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>The applied files before an activation, so a failed switch can be undone exactly.</summary>
    private sealed record RuntimeSnapshot(string? Config, string? Template, string? StructureHash)
    {
        public static RuntimeSnapshot Capture(XrayOptions options) => new(
            File.Exists(options.RuntimeConfigPath) ? File.ReadAllText(options.RuntimeConfigPath) : null,
            File.Exists(options.RuntimeTemplatePath) ? File.ReadAllText(options.RuntimeTemplatePath) : null,
            ReadTrimmed(options.StructureHashPath));

        public async Task RestoreAsync(XrayOptions options)
        {
            if (Config is not null) await WriteAtomicAsync(options.RuntimeConfigPath, Config);
            if (Template is not null) await WriteAtomicAsync(options.RuntimeTemplatePath, Template);
            if (StructureHash is not null) await WriteAtomicAsync(options.StructureHashPath, StructureHash);
        }
    }
}
