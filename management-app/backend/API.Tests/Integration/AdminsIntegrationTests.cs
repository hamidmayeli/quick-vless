using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration;

public sealed class AdminsIntegrationTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _super;

    public AdminsIntegrationTests()
    {
        _super = TestAuth.CreateSuperAdminClientAsync(_factory).GetAwaiter().GetResult();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task CreateAdmin_AsSuperAdmin_Returns201Pending()
    {
        var response = await _super.PostAsJsonAsync("/api/v1/admins", new { username = "alice" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var admin = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("alice", admin.GetProperty("username").GetString());
        Assert.Equal("admin", admin.GetProperty("role").GetString());
        Assert.Equal("pending", admin.GetProperty("status").GetString());
        Assert.Equal(0, admin.GetProperty("user_count").GetInt32());
    }

    [Fact]
    public async Task CreateAdmin_TrimsUsername()
    {
        var response = await _super.PostAsJsonAsync("/api/v1/admins", new { username = "  alice  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var admin = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("alice", admin.GetProperty("username").GetString());
    }

    [Fact]
    public async Task AdminResponses_NeverContainSecrets()
    {
        var created = await _super.PostAsJsonAsync("/api/v1/admins", new { username = "alice" });
        await TestAuth.LoginAsync(_factory.CreateClient(), "alice", "alice-pass");
        var list = await _super.GetAsync("/api/v1/admins");

        foreach (var body in new[] { await created.Content.ReadAsStringAsync(), await list.Content.ReadAsStringAsync() })
        {
            Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("salt", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("refresh", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("ALICE")]
    public async Task CreateAdmin_Duplicate_Returns409(string duplicate)
    {
        await TestAuth.CreateAdminAsync(_super, "alice");

        var response = await _super.PostAsJsonAsync("/api/v1/admins", new { username = duplicate });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(TestAuth.SuperAdmin)]
    [InlineData("ADMIN")]
    public async Task CreateAdmin_SuperAdminUsername_Returns409(string username)
    {
        var response = await _super.PostAsJsonAsync("/api/v1/admins", new { username });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a b")]
    [InlineData("x/y")]
    [InlineData("名前")]
    public async Task CreateAdmin_InvalidUsername_Returns400(string? username)
    {
        var response = await _super.PostAsJsonAsync("/api/v1/admins", new { username });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAdmin_UsernameLongerThan64_Returns400()
    {
        var response = await _super.PostAsJsonAsync("/api/v1/admins", new { username = new string('a', 65) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListAdmins_SuperAdminFirstWithStatusesAndUserCounts()
    {
        await TestAuth.CreateAdminAsync(_super, "zed");
        var bob = await TestAuth.CreateAdminClientAsync(_factory, _super, "bob");
        await TestAuth.CreateUserAsync(bob, "bob-user-1");
        await TestAuth.CreateUserAsync(bob, "bob-user-2");

        var admins = await _super.GetFromJsonAsync<JsonElement[]>("/api/v1/admins");

        Assert.Equal(new[] { "admin", "bob", "zed" }, admins!.Select(a => a.GetProperty("username").GetString()).ToArray());
        Assert.Equal(new[] { "super_admin", "admin", "admin" }, admins!.Select(a => a.GetProperty("role").GetString()).ToArray());
        Assert.Equal(new[] { "active", "active", "pending" }, admins!.Select(a => a.GetProperty("status").GetString()).ToArray());
        Assert.Equal(new[] { 0, 2, 0 }, admins!.Select(a => a.GetProperty("user_count").GetInt32()).ToArray());
    }

    [Fact]
    public async Task AdminsEndpoints_AsNormalAdmin_Return403()
    {
        await TestAuth.CreateAdminAsync(_super, "victim");
        var alice = await TestAuth.CreateAdminClientAsync(_factory, _super, "alice");

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/v1/admins")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await alice.PostAsJsonAsync("/api/v1/admins", new { username = "mallory" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.DeleteAsync("/api/v1/admins/victim")).StatusCode);
        var admins = await _super.GetFromJsonAsync<JsonElement[]>("/api/v1/admins");
        Assert.Equal(3, admins!.Length);
    }

    [Fact]
    public async Task AdminsEndpoints_Unauthenticated_Return401()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admins")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/v1/admins", new { username = "mallory" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync("/api/v1/admins/admin")).StatusCode);
    }

    [Fact]
    public async Task DeleteAdmin_ReassignsTheirUsersToSuperAdmin()
    {
        var bob = await TestAuth.CreateAdminClientAsync(_factory, _super, "bob");
        var user = await TestAuth.CreateUserAsync(bob, "bob-user");

        var response = await _super.DeleteAsync("/api/v1/admins/bob");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var users = await _super.GetFromJsonAsync<JsonElement[]>("/api/v1/users");
        var reassigned = Assert.Single(users!, u => u.GetProperty("id").GetString() == user.GetProperty("id").GetString());
        Assert.Equal(TestAuth.SuperAdmin, reassigned.GetProperty("created_by").GetString());
        var admins = await _super.GetFromJsonAsync<JsonElement[]>("/api/v1/admins");
        Assert.DoesNotContain(admins!, a => a.GetProperty("username").GetString() == "bob");
    }

    [Fact]
    public async Task DeleteAdmin_PendingAdmin_Returns204()
    {
        await TestAuth.CreateAdminAsync(_super, "alice");

        var response = await _super.DeleteAsync("/api/v1/admins/alice");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAdmin_SuperAdmin_Returns400()
    {
        var response = await _super.DeleteAsync($"/api/v1/admins/{TestAuth.SuperAdmin}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _super.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task DeleteAdmin_Unknown_Returns404()
    {
        var response = await _super.DeleteAsync("/api/v1/admins/nobody");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAdmin_ThenLoginWithOldCredentials_Returns401()
    {
        await TestAuth.CreateAdminClientAsync(_factory, _super, "alice", "alice-pass");

        await _super.DeleteAsync("/api/v1/admins/alice");
        var response = await TestAuth.PostLoginAsync(_factory.CreateClient(), "alice", "alice-pass");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
