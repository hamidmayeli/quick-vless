using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration;

internal static class TestAuth
{
    // The first login on an empty store creates the super admin.
    public const string SuperAdmin = "admin";
    public const string SuperAdminPassword = "pass";

    public static Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string username, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { username, password });

    public static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await PostLoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("access_token").GetString()!;
    }

    public static async Task<HttpClient> CreateClientAsAsync(CustomWebApplicationFactory factory, string username, string password)
    {
        var client = factory.CreateClient();
        var token = await LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static Task<HttpClient> CreateSuperAdminClientAsync(CustomWebApplicationFactory factory) =>
        CreateClientAsAsync(factory, SuperAdmin, SuperAdminPassword);

    public static async Task CreateAdminAsync(HttpClient superClient, string username)
    {
        var response = await superClient.PostAsJsonAsync("/api/v1/admins", new { username });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>Creates a pending admin as the super admin, then signs in as them (which sets their password).</summary>
    public static async Task<HttpClient> CreateAdminClientAsync(
        CustomWebApplicationFactory factory, HttpClient superClient, string username, string password = "admin-pass")
    {
        await CreateAdminAsync(superClient, username);
        return await CreateClientAsAsync(factory, username, password);
    }

    public static string? ReadClaim(string jwt, string claimType) =>
        new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims.FirstOrDefault(c => c.Type == claimType)?.Value;

    public static string RefreshTokenFrom(HttpResponseMessage loginResponse) =>
        Assert.Single(loginResponse.Headers.GetValues("Set-Cookie")).Split(';')[0].Split('=', 2)[1];

    public static async Task<JsonElement> CreateUserAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/users", new
        {
            name,
            quota = (long?)null,
            expiry = (string?)null,
            single_connection = false,
            enabled = false,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
