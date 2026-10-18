using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration;

public sealed class UsersIntegrationTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public UsersIntegrationTests()
    {
        // The bootstrap login makes this client the super admin.
        _client = TestAuth.CreateSuperAdminClientAsync(_factory).GetAwaiter().GetResult();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GetUsers_EmptyState_ReturnsEmptyArray()
    {
        var response = await _client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var users = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.Empty(users!);
    }

    [Fact]
    public async Task GetUsers_WithoutAuth_Returns401()
    {
        var noAuth = _factory.CreateClient();
        var response = await noAuth.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_ValidRequest_Returns201WithUser()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users", new
        {
            name = "TestUser",
            quota = 50L * 1_073_741_824L,
            expiry = (string?)null,
            single_connection = false,
            enabled = false,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TestUser", user.GetProperty("name").GetString());
        Assert.Equal(50L * 1_073_741_824L, user.GetProperty("quota").GetInt64());
        Assert.True(user.TryGetProperty("id", out var id) && !string.IsNullOrEmpty(id.GetString()));
    }

    [Fact]
    public async Task CreateUser_AppearsInList()
    {
        await _client.PostAsJsonAsync("/api/v1/users", new
        {
            name = "ListUser",
            quota = (double?)null,
            expiry = (string?)null,
            single_connection = false,
            enabled = false,
        });

        var list = await _client.GetFromJsonAsync<JsonElement[]>("/api/v1/users");
        Assert.Contains(list!, u => u.GetProperty("name").GetString() == "ListUser");
    }

    [Fact]
    public async Task UpdateUser_ExistingUser_ReturnsUpdated()
    {
        var created = await CreateTestUser("OriginalName");
        var id = created.GetProperty("id").GetString()!;

        var response = await _client.PutAsJsonAsync($"/api/v1/users/{id}", new
        {
            name = "UpdatedName",
            quota = 100L * 1_073_741_824L,
            expiry = (string?)null,
            single_connection = false,
            enabled = false,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UpdatedName", updated.GetProperty("name").GetString());
    }

    [Fact]
    public async Task UpdateUser_NonExistent_Returns404()
    {
        var response = await _client.PutAsJsonAsync("/api/v1/users/nonexistent-id", new
        {
            name = "X",
            quota = (double?)null,
            expiry = (string?)null,
            single_connection = false,
            enabled = false,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_ExistingUser_Returns204AndRemoves()
    {
        var created = await CreateTestUser("DeleteMe");
        var id = created.GetProperty("id").GetString()!;

        var delete = await _client.DeleteAsync($"/api/v1/users/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var list = await _client.GetFromJsonAsync<JsonElement[]>("/api/v1/users");
        Assert.DoesNotContain(list!, u => u.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task DeleteUser_NonExistent_Returns404()
    {
        var response = await _client.DeleteAsync("/api/v1/users/nonexistent-id");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_SetsCreatedByToCaller()
    {
        var alice = await TestAuth.CreateAdminClientAsync(_factory, _client, "alice");

        var bySuper = await TestAuth.CreateUserAsync(_client, "SuperUser");
        var byAlice = await TestAuth.CreateUserAsync(alice, "AliceUser");

        Assert.Equal(TestAuth.SuperAdmin, bySuper.GetProperty("created_by").GetString());
        Assert.Equal("alice", byAlice.GetProperty("created_by").GetString());
    }

    [Fact]
    public async Task NormalAdmin_ListUsers_ReturnsOnlyOwnUsers()
    {
        var alice = await TestAuth.CreateAdminClientAsync(_factory, _client, "alice");
        var bob = await TestAuth.CreateAdminClientAsync(_factory, _client, "bob");
        await TestAuth.CreateUserAsync(_client, "SuperUser");
        await TestAuth.CreateUserAsync(alice, "AliceUser");
        await TestAuth.CreateUserAsync(bob, "BobUser");

        var aliceList = await alice.GetFromJsonAsync<JsonElement[]>("/api/v1/users");
        var bobList = await bob.GetFromJsonAsync<JsonElement[]>("/api/v1/users");

        Assert.Equal("AliceUser", Assert.Single(aliceList!).GetProperty("name").GetString());
        Assert.Equal("BobUser", Assert.Single(bobList!).GetProperty("name").GetString());
    }

    [Fact]
    public async Task SuperAdmin_ListUsers_ReturnsAllUsersIncludingOtherAdmins()
    {
        var alice = await TestAuth.CreateAdminClientAsync(_factory, _client, "alice");
        await TestAuth.CreateUserAsync(_client, "SuperUser");
        await TestAuth.CreateUserAsync(alice, "AliceUser");

        var list = await _client.GetFromJsonAsync<JsonElement[]>("/api/v1/users");

        Assert.Equal(
            new[] { "AliceUser", "SuperUser" },
            list!.Select(u => u.GetProperty("name").GetString()).Order().ToArray());
    }

    [Fact]
    public async Task NormalAdmin_UpdateOtherAdminsUser_Returns404AndLeavesUserUnchanged()
    {
        var alice = await TestAuth.CreateAdminClientAsync(_factory, _client, "alice");
        var bob = await TestAuth.CreateAdminClientAsync(_factory, _client, "bob");
        var bobUser = await TestAuth.CreateUserAsync(bob, "BobUser");
        var id = bobUser.GetProperty("id").GetString()!;

        var response = await alice.PutAsJsonAsync($"/api/v1/users/{id}", new { name = "Hijacked", enabled = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var stored = Assert.Single((await bob.GetFromJsonAsync<JsonElement[]>("/api/v1/users"))!);
        Assert.Equal("BobUser", stored.GetProperty("name").GetString());
        Assert.False(stored.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task NormalAdmin_DeleteSuperAdminsUser_Returns404AndUserStillExists()
    {
        var alice = await TestAuth.CreateAdminClientAsync(_factory, _client, "alice");
        var superUser = await TestAuth.CreateUserAsync(_client, "SuperUser");
        var id = superUser.GetProperty("id").GetString()!;

        var response = await alice.DeleteAsync($"/api/v1/users/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var list = await _client.GetFromJsonAsync<JsonElement[]>("/api/v1/users");
        Assert.Contains(list!, u => u.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task NormalAdmin_UpdateAndDeleteOwnUser_Succeeds()
    {
        var alice = await TestAuth.CreateAdminClientAsync(_factory, _client, "alice");
        var user = await TestAuth.CreateUserAsync(alice, "AliceUser");
        var id = user.GetProperty("id").GetString()!;

        var update = await alice.PutAsJsonAsync($"/api/v1/users/{id}", new { name = "Renamed" });
        var delete = await alice.DeleteAsync($"/api/v1/users/{id}");

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_UpdateAdminsUser_Returns200AndKeepsCreatedBy()
    {
        var alice = await TestAuth.CreateAdminClientAsync(_factory, _client, "alice");
        var user = await TestAuth.CreateUserAsync(alice, "AliceUser");
        var id = user.GetProperty("id").GetString()!;

        var response = await _client.PutAsJsonAsync($"/api/v1/users/{id}", new { name = "EditedBySuper" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("EditedBySuper", updated.GetProperty("name").GetString());
        Assert.Equal("alice", updated.GetProperty("created_by").GetString());
    }

    [Fact]
    public async Task UpdateUser_BodyWithCreatedBy_IsIgnored()
    {
        var alice = await TestAuth.CreateAdminClientAsync(_factory, _client, "alice");
        var user = await TestAuth.CreateUserAsync(alice, "AliceUser");
        var id = user.GetProperty("id").GetString()!;

        await alice.PutAsJsonAsync($"/api/v1/users/{id}", new { name = "AliceUser", created_by = "bob" });

        var stored = Assert.Single((await alice.GetFromJsonAsync<JsonElement[]>("/api/v1/users"))!);
        Assert.Equal("alice", stored.GetProperty("created_by").GetString());
    }

    [Fact]
    public async Task LegacyUsersWithoutOwner_VisibleToSuperAdminOnly()
    {
        using var factory = new CustomWebApplicationFactory();
        factory.SeedFile("users.json", """
            [{ "id": "legacy-1", "name": "LegacyUser", "secret": "legacy-secret", "quota": null,
               "expiry": null, "single_connection": false, "enabled": false }]
            """);
        var super = await TestAuth.CreateSuperAdminClientAsync(factory);
        var alice = await TestAuth.CreateAdminClientAsync(factory, super, "alice");

        var superList = await super.GetFromJsonAsync<JsonElement[]>("/api/v1/users");
        var aliceList = await alice.GetFromJsonAsync<JsonElement[]>("/api/v1/users");

        var legacy = Assert.Single(superList!);
        Assert.Equal(JsonValueKind.Null, legacy.GetProperty("created_by").ValueKind);
        Assert.Empty(aliceList!);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync("/api/v1/users/legacy-1")).StatusCode);
    }

    private Task<JsonElement> CreateTestUser(string name) => TestAuth.CreateUserAsync(_client, name);
}
