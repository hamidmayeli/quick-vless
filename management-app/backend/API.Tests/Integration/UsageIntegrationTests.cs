using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration;

public sealed class UsageIntegrationTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public UsageIntegrationTests()
    {
        _client = TestAuth.CreateSuperAdminClientAsync(_factory).GetAwaiter().GetResult();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GetUsage_EmptyState_ReturnsEmptyArray()
    {
        var response = await _client.GetAsync("/api/v1/usage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var records = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.Empty(records!);
    }

    [Fact]
    public async Task GetUsage_WithoutAuth_Returns401()
    {
        var noAuth = _factory.CreateClient();
        var response = await noAuth.GetAsync("/api/v1/usage");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NormalAdmin_GetUsage_ReturnsOnlyRecordsOfOwnUsers()
    {
        using var factory = CreateSeededFactory();
        var super = await TestAuth.CreateSuperAdminClientAsync(factory);
        var alice = await TestAuth.CreateAdminClientAsync(factory, super, "alice");

        var records = await alice.GetFromJsonAsync<JsonElement[]>("/api/v1/usage");

        Assert.Equal(new[] { "u-alice", "u-alice" }, UserIds(records!));
    }

    [Fact]
    public async Task SuperAdmin_GetUsage_ReturnsAllRecordsIncludingLegacyAndDeletedUsers()
    {
        using var factory = CreateSeededFactory();
        var super = await TestAuth.CreateSuperAdminClientAsync(factory);

        var records = await super.GetFromJsonAsync<JsonElement[]>("/api/v1/usage");

        Assert.Equal(new[] { "u-alice", "u-alice", "u-bob", "u-deleted", "u-legacy" }, UserIds(records!));
    }

    [Fact]
    public async Task NormalAdmin_WithNoUsers_GetUsage_ReturnsEmpty()
    {
        using var factory = CreateSeededFactory();
        var super = await TestAuth.CreateSuperAdminClientAsync(factory);
        var carol = await TestAuth.CreateAdminClientAsync(factory, super, "carol");

        var records = await carol.GetFromJsonAsync<JsonElement[]>("/api/v1/usage");

        Assert.Empty(records!);
    }

    // Users owned by alice and bob, one legacy user without an owner,
    // and usage for all of them plus a user that no longer exists.
    private static CustomWebApplicationFactory CreateSeededFactory()
    {
        var factory = new CustomWebApplicationFactory();
        factory.SeedFile("users.json", """
            [
              { "id": "u-alice", "name": "AliceUser", "secret": "s-alice", "enabled": false, "created_by": "alice" },
              { "id": "u-bob", "name": "BobUser", "secret": "s-bob", "enabled": false, "created_by": "bob" },
              { "id": "u-legacy", "name": "LegacyUser", "secret": "s-legacy", "enabled": false }
            ]
            """);
        var now = DateTime.UtcNow;
        var timestamp = now.ToString("O", CultureInfo.InvariantCulture);
        var fileName = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv";
        factory.SeedFile(Path.Combine("usage_history", fileName), string.Join(Environment.NewLine,
            $"u-alice,{timestamp},0,0,100",
            $"u-alice,{timestamp},0,0,200",
            $"u-bob,{timestamp},0,0,300",
            $"u-legacy,{timestamp},0,0,400",
            $"u-deleted,{timestamp},0,0,500"));
        return factory;
    }

    private static string?[] UserIds(JsonElement[] records) =>
        records.Select(r => r.GetProperty("user_id").GetString()).Order().ToArray();
}
