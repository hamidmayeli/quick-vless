using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration;

public sealed class UsersIntegrationTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public UsersIntegrationTests()
    {
        _client = _factory.CreateClient();
        SetupAuth().GetAwaiter().GetResult();
    }

    public void Dispose() => _factory.Dispose();

    private async Task SetupAuth()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "pass" });
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var token = json.GetProperty("access_token").GetString()!;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

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

    private async Task<JsonElement> CreateTestUser(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users", new
        {
            name,
            quota = (double?)null,
            expiry = (string?)null,
            single_connection = false,
            enabled = false,
        });
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
