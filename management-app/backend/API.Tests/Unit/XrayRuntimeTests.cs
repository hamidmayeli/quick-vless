using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using API.Models;
using API.Services;
using API.Services.Xray;
using API.Storage;

namespace API.Tests.Unit;

public sealed class XrayRuntimeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "easy-xray-runtime-" + Guid.NewGuid());
    private readonly FakeXrayClient _xray = new();
    private readonly FakeConfigTester _tester = new();
    private readonly FakeRestartMonitor _monitor = new();
    private readonly UserRepository _users;
    private readonly SettingsRepository _settings;

    private string ProfilesDir => Path.Combine(_dir, "profiles");
    private string RuntimeDir => Path.Combine(_dir, "runtime");
    private string LegacyConfigPath => Path.Combine(_dir, "legacy", "config.json");

    public XrayRuntimeTests()
    {
        TestProfiles.WriteAll(ProfilesDir);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:UsersPath"] = Path.Combine(_dir, "users.json"),
                ["Storage:SettingsPath"] = Path.Combine(_dir, "settings.json"),
            })
            .Build();
        _users = new UserRepository(config);
        _settings = new SettingsRepository(config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private XrayOptions Options(bool profileMode = true) => new()
    {
        ProfilesDir = profileMode ? ProfilesDir : null,
        RuntimeDir = RuntimeDir,
        DefaultProfile = TestProfiles.Reality,
        RestartConfirmTimeout = TimeSpan.FromSeconds(5),
        LegacyConfigPath = LegacyConfigPath,
        LegacyInboundTag = "vless-in",
    };

    private XrayRuntime CreateRuntime(bool profileMode = true)
    {
        var options = Options(profileMode);
        return new XrayRuntime(options, new XrayProfileCatalog(options), _users, _settings,
            _xray, _tester, _monitor, NullLogger<XrayRuntime>.Instance);
    }

    private async Task<User> AddUserAsync(string name, bool enabled = true)
    {
        var user = new User { Id = "id-" + name, Name = name, Secret = "secret-" + name, Enabled = enabled };
        await _users.AddAsync(user);
        return user;
    }

    private string RuntimeFile(string name) => File.ReadAllText(Path.Combine(RuntimeDir, name));

    private JsonObject RuntimeConfig() => (JsonObject)JsonNode.Parse(RuntimeFile("config.json"))!;

    private static JsonArray ClientsOf(JsonObject config, string tag) =>
        config["inbounds"]!.AsArray().OfType<JsonObject>()
            .Single(i => XrayRuntimeConfigBuilder.TagOf(i) == tag)["settings"]!["clients"]!.AsArray();

    [Fact]
    public async Task Initialize_AppliesTheDefaultProfileAndRemembersIt()
    {
        var user = await AddUserAsync("alice");
        var runtime = CreateRuntime();

        await runtime.InitializeAsync();

        Assert.Equal(TestProfiles.Reality, runtime.SelectedProfile);
        Assert.Null(runtime.LastError);
        var client = Assert.Single(ClientsOf(RuntimeConfig(), "vless-in"));
        Assert.Equal(user.Secret, client!["email"]!.GetValue<string>());
        Assert.Equal(TestProfiles.RealityTemplate, RuntimeFile("client-config.template"));
        Assert.Equal(64, RuntimeFile("structure.sha256").Length);
        Assert.Equal(TestProfiles.Reality, (await _settings.GetAsync()).XrayActiveProfile);
        Assert.Contains(("add", user.Secret, "vless-in", "xtls-rprx-vision"), _xray.Calls);
        Assert.Equal(1, _tester.Calls);
    }

    [Fact]
    public async Task Initialize_UnchangedStructure_DoesNotTestAgain()
    {
        await CreateRuntime().InitializeAsync();

        await CreateRuntime().InitializeAsync();

        Assert.Equal(1, _tester.Calls);
    }

    [Fact]
    public async Task Initialize_InvalidActiveProfile_KeepsTheLastGoodConfig()
    {
        await CreateRuntime().InitializeAsync();
        var good = RuntimeFile("config.json");
        await _settings.UpdateAsync(s => s.XrayActiveProfile = TestProfiles.Broken);

        var runtime = CreateRuntime();
        await runtime.InitializeAsync();

        Assert.Equal(good, RuntimeFile("config.json"));
        Assert.Contains("is invalid", runtime.LastError);
        Assert.Equal(TestProfiles.RealityTemplate, runtime.ClientTemplate);
    }

    [Fact]
    public async Task Activate_SwitchesTheConfigTemplateAndUserFlow()
    {
        var user = await AddUserAsync("alice");
        var runtime = CreateRuntime();
        await runtime.InitializeAsync();

        var result = await runtime.ActivateAsync(TestProfiles.WebSocket);

        Assert.Equal(ActivationStatus.Activated, result.Status);
        Assert.Equal(TestProfiles.WebSocket, runtime.SelectedProfile);
        Assert.Equal(TestProfiles.WebSocketTemplate, runtime.ClientTemplate);
        var client = Assert.Single(ClientsOf(RuntimeConfig(), "vless-cf"))!.AsObject();
        Assert.False(client.ContainsKey("flow"));
        Assert.Equal(TestProfiles.WebSocket, (await _settings.GetAsync()).XrayActiveProfile);
        Assert.Equal(RuntimeFile("structure.sha256"), Assert.Single(_monitor.WaitedFor));

        _xray.Calls.Clear();
        await runtime.UserAddedAsync(await AddUserAsync("bob"));
        Assert.Equal(new[] { ("add", "secret-bob", "vless-cf", "") }, _xray.Calls.ToArray());
        Assert.NotNull(user);
    }

    [Fact]
    public async Task Activate_ConfigRejectedByXray_LeavesTheRunningConfigUntouched()
    {
        var runtime = CreateRuntime();
        await runtime.InitializeAsync();
        var before = RuntimeFile("config.json");
        _tester.Result = new XrayConfigTestResult(false, "Failed to start: bad certificate");

        var result = await runtime.ActivateAsync(TestProfiles.WebSocket);

        Assert.Equal(ActivationStatus.Invalid, result.Status);
        Assert.Contains("Failed to start: bad certificate", Assert.Single(result.Errors!));
        Assert.Equal(before, RuntimeFile("config.json"));
        Assert.False(File.Exists(Path.Combine(RuntimeDir, "config.next.json")));
        Assert.Equal(TestProfiles.Reality, runtime.SelectedProfile);
    }

    [Fact]
    public async Task Activate_RestartNotConfirmed_RollsBackToThePreviousProfile()
    {
        var runtime = CreateRuntime();
        await runtime.InitializeAsync();
        var before = RuntimeFile("config.json");
        var beforeHash = RuntimeFile("structure.sha256");
        _monitor.Confirm = hash => hash == beforeHash;

        var result = await runtime.ActivateAsync(TestProfiles.WebSocket);

        Assert.Equal(ActivationStatus.RolledBack, result.Status);
        Assert.Contains("Rolled back to \"vless-reality\".", result.Message);
        Assert.Equal(before, RuntimeFile("config.json"));
        Assert.Equal(beforeHash, RuntimeFile("structure.sha256"));
        Assert.Equal(TestProfiles.RealityTemplate, RuntimeFile("client-config.template"));
        Assert.Equal(TestProfiles.Reality, runtime.SelectedProfile);
        Assert.Equal(TestProfiles.RealityTemplate, runtime.ClientTemplate);
        Assert.Equal(result.Message, runtime.LastError);
        Assert.Equal(TestProfiles.Reality, (await _settings.GetAsync()).XrayActiveProfile);
    }

    [Fact]
    public async Task Activate_InvalidOrUnknownProfile()
    {
        var runtime = CreateRuntime();
        await runtime.InitializeAsync();

        var invalid = await runtime.ActivateAsync(TestProfiles.Broken);
        var unknown = await runtime.ActivateAsync("nope");

        Assert.Equal(ActivationStatus.Invalid, invalid.Status);
        Assert.Contains(invalid.Errors!, e => e.Contains("YOUR_PRIVATE_KEY"));
        Assert.Equal(ActivationStatus.NotFound, unknown.Status);
    }

    [Fact]
    public async Task Activate_InLegacyMode_IsRefused()
    {
        var result = await CreateRuntime(profileMode: false).ActivateAsync(TestProfiles.Reality);

        Assert.Equal(ActivationStatus.LegacyMode, result.Status);
    }

    [Fact]
    public async Task Activate_WhileAnotherSwitchRuns_IsBusy()
    {
        var runtime = CreateRuntime();
        await runtime.InitializeAsync();
        var release = new TaskCompletionSource<bool>();
        _monitor.Wait = _ => release.Task;

        var first = runtime.ActivateAsync(TestProfiles.WebSocket);
        await _monitor.Entered.Task;
        var second = await runtime.ActivateAsync(TestProfiles.Reality);
        release.SetResult(true);

        Assert.Equal(ActivationStatus.Busy, second.Status);
        Assert.Equal(ActivationStatus.Activated, (await first).Status);
    }

    [Fact]
    public async Task UserChange_DuringASwitch_IsAppliedToTheNewProfile()
    {
        var runtime = CreateRuntime();
        await runtime.InitializeAsync();
        var release = new TaskCompletionSource<bool>();
        _monitor.Wait = _ => release.Task;

        var switching = runtime.ActivateAsync(TestProfiles.WebSocket);
        await _monitor.Entered.Task;
        var bob = await AddUserAsync("bob");
        var change = runtime.UserAddedAsync(bob);
        await Task.Delay(100);
        Assert.False(change.IsCompleted);

        _xray.Calls.Clear();
        release.SetResult(true);
        await switching;
        await change;

        Assert.Equal(new[] { ("add", "secret-bob", "vless-cf", "") }, _xray.Calls.ToArray());
        Assert.Contains(ClientsOf(RuntimeConfig(), "vless-cf"), c => c!["id"]!.GetValue<string>() == "secret-bob");
    }

    [Fact]
    public async Task UserChanges_RewriteTheRuntimeConfigAndCallXray()
    {
        var runtime = CreateRuntime();
        await runtime.InitializeAsync();
        var alice = await AddUserAsync("alice");
        _xray.Calls.Clear();

        await runtime.UserAddedAsync(alice);
        Assert.Single(ClientsOf(RuntimeConfig(), "vless-in"));

        alice.Enabled = false;
        await _users.UpdateAsync(alice);
        await runtime.UserUpdatedAsync(alice, wasEnabled: true);
        Assert.Empty(ClientsOf(RuntimeConfig(), "vless-in"));

        Assert.Equal(
            new[] { ("add", "secret-alice", "vless-in", "xtls-rprx-vision"), ("remove", "secret-alice", "vless-in", "") },
            _xray.Calls.ToArray());
    }

    [Fact]
    public async Task SecretRotated_ReplacesTheSecretInTheFileAndInXray()
    {
        var runtime = CreateRuntime();
        var alice = await AddUserAsync("alice");
        await runtime.InitializeAsync();
        _xray.Calls.Clear();

        alice.Secret = "rotated";
        await _users.UpdateAsync(alice);
        await runtime.SecretRotatedAsync(alice, "secret-alice");

        Assert.Equal("rotated", Assert.Single(ClientsOf(RuntimeConfig(), "vless-in"))!["id"]!.GetValue<string>());
        Assert.Equal(
            new[] { ("remove", "secret-alice", "vless-in", ""), ("add", "rotated", "vless-in", "xtls-rprx-vision") },
            _xray.Calls.ToArray());
    }

    [Fact]
    public async Task UserChange_DoesNotTouchTheStructureHash()
    {
        var runtime = CreateRuntime();
        await runtime.InitializeAsync();
        var hash = RuntimeFile("structure.sha256");

        await runtime.UserAddedAsync(await AddUserAsync("alice"));

        Assert.Equal(hash, RuntimeFile("structure.sha256"));
    }

    [Fact]
    public async Task IsApplied_ComparesTheRunningAndWantedHashes()
    {
        var runtime = CreateRuntime();
        await runtime.InitializeAsync();
        Assert.False(runtime.IsApplied);

        File.WriteAllText(Path.Combine(RuntimeDir, "running.sha256"), RuntimeFile("structure.sha256") + "\n");

        Assert.True(runtime.IsApplied);
        Assert.Null(CreateRuntime(profileMode: false).IsApplied);
    }

    [Fact]
    public async Task LegacyMode_PatchesOnlyTheConfiguredInboundWithSecretEmails()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LegacyConfigPath)!);
        File.WriteAllText(LegacyConfigPath, TestProfiles.RealityConfig);
        var alice = await AddUserAsync("alice");
        var runtime = CreateRuntime(profileMode: false);

        await runtime.InitializeAsync();

        var config = (JsonObject)JsonNode.Parse(File.ReadAllText(LegacyConfigPath))!;
        var client = Assert.Single(ClientsOf(config, "vless-in"))!;
        Assert.Equal(alice.Secret, client["email"]!.GetValue<string>());
        Assert.Null(config["api"]);
        Assert.Equal(new[] { ("add", alice.Secret, "vless-in", "xtls-rprx-vision") }, _xray.Calls.ToArray());
        Assert.False(Directory.Exists(RuntimeDir));
    }

    [Fact]
    public async Task UrlGenerator_RendersTheAppliedTemplate()
    {
        var runtime = CreateRuntime();
        var generator = new VlessUrlGenerator(new ConfigurationBuilder().Build(), runtime);
        Assert.False(generator.CanGenerate);

        await runtime.InitializeAsync();
        await runtime.ActivateAsync(TestProfiles.WebSocket);

        Assert.Equal(
            TestProfiles.WebSocketTemplate.Replace("{{secret}}", "s").Replace("{{name}}", "Ann%20B"),
            generator.Generate("s", "Ann B"));
    }

    private sealed class FakeXrayClient : IXrayClient
    {
        public List<(string Op, string Secret, string Tag, string Flow)> Calls { get; } = [];

        public Task<bool> AddUserAsync(User user, string tag, string flow)
        {
            lock (Calls) Calls.Add(("add", user.Secret, tag, flow));
            return Task.FromResult(true);
        }

        public Task<bool> RemoveUserAsync(string userId, string secret, string tag)
        {
            lock (Calls) Calls.Add(("remove", secret, tag, ""));
            return Task.FromResult(true);
        }

        public Task<bool> ProbeAsync() => Task.FromResult(true);
    }

    private sealed class FakeConfigTester : IXrayConfigTester
    {
        public XrayConfigTestResult Result { get; set; } = new(true, null);
        public int Calls { get; private set; }

        public Task<XrayConfigTestResult> TestAsync(string configPath, CancellationToken ct = default)
        {
            Assert.True(File.Exists(configPath));
            // xray picks the config format from the extension; anything else fails with "Failed to get format".
            Assert.EndsWith(".json", configPath);
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeRestartMonitor : IXrayRestartMonitor
    {
        public Func<string, bool> Confirm { get; set; } = _ => true;
        public Func<string, Task<bool>>? Wait { get; set; }
        public ConcurrentQueue<string> WaitedFor { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsEnabled => true;

        public Task<bool> WaitForAsync(string structureHash, CancellationToken ct = default)
        {
            WaitedFor.Enqueue(structureHash);
            Entered.TrySetResult();
            return Wait?.Invoke(structureHash) ?? Task.FromResult(Confirm(structureHash));
        }
    }
}
