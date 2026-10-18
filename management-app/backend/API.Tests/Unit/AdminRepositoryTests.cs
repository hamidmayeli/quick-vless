using System.Text.Json;
using Microsoft.Extensions.Configuration;
using API.Models;
using API.Storage;

namespace API.Tests.Unit;

public sealed class AdminRepositoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "easy-xray-admins-" + Guid.NewGuid());

    private string AdminsPath => Path.Combine(_tempDir, "admins.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private AdminRepository CreateRepo() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:AdminsPath"] = AdminsPath })
            .Build());

    private static Admin Pending(string username) => new() { Username = username, Role = AdminRole.Admin };

    private void WriteRawFile(string json)
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(AdminsPath, json);
    }

    [Fact]
    public async Task CreateFirstSuperAdmin_EmptyStore_CreatesSuperAdmin()
    {
        var repo = CreateRepo();

        var admin = await repo.CreateFirstSuperAdminAsync("root", "hash", "salt");

        Assert.NotNull(admin);
        var stored = Assert.Single(await repo.GetAllAsync());
        Assert.Equal("root", stored.Username);
        Assert.Equal(AdminRole.SuperAdmin, stored.Role);
        Assert.Equal("hash", stored.PasswordHash);
        Assert.False(stored.IsPending);
    }

    [Fact]
    public async Task CreateFirstSuperAdmin_AdminsExist_ReturnsNull()
    {
        var repo = CreateRepo();
        await repo.CreateFirstSuperAdminAsync("root", "hash", "salt");

        var second = await repo.CreateFirstSuperAdminAsync("intruder", "hash", "salt");

        Assert.Null(second);
        Assert.Single(await repo.GetAllAsync());
    }

    [Fact]
    public async Task CreateFirstSuperAdmin_ConcurrentCalls_ExactlyOneSucceeds()
    {
        var repo = CreateRepo();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            Task.Run(() => repo.CreateFirstSuperAdminAsync($"racer{i}", "hash", "salt"))));

        Assert.Single(results, r => r is not null);
        Assert.Single(await repo.GetAllAsync());
    }

    [Fact]
    public async Task AddAsync_NewUsername_PersistsAsPendingAdmin()
    {
        var repo = CreateRepo();

        var added = await repo.AddAsync(Pending("alice"));

        Assert.True(added);
        var stored = await repo.GetByUsernameAsync("alice");
        Assert.NotNull(stored);
        Assert.Equal(AdminRole.Admin, stored.Role);
        Assert.True(stored.IsPending);
        Assert.Null(stored.PasswordSalt);
    }

    [Fact]
    public async Task AddAsync_DuplicateUsernameDifferentCase_ReturnsFalse()
    {
        var repo = CreateRepo();
        await repo.AddAsync(Pending("alice"));

        var added = await repo.AddAsync(Pending("ALICE"));

        Assert.False(added);
        Assert.Single(await repo.GetAllAsync());
    }

    [Fact]
    public async Task GetByUsername_IsCaseInsensitive()
    {
        var repo = CreateRepo();
        await repo.AddAsync(Pending("alice"));

        var stored = await repo.GetByUsernameAsync("Alice");

        Assert.Equal("alice", stored?.Username);
    }

    [Fact]
    public async Task SetInitialPassword_PendingAdmin_SetsHashAndSalt()
    {
        var repo = CreateRepo();
        await repo.AddAsync(Pending("alice"));

        var admin = await repo.SetInitialPasswordAsync("alice", "hash", "salt");

        Assert.NotNull(admin);
        var stored = await repo.GetByUsernameAsync("alice");
        Assert.Equal("hash", stored!.PasswordHash);
        Assert.Equal("salt", stored.PasswordSalt);
        Assert.False(stored.IsPending);
    }

    [Fact]
    public async Task SetInitialPassword_ActiveAdmin_ReturnsNullAndKeepsOriginalHash()
    {
        var repo = CreateRepo();
        await repo.AddAsync(Pending("alice"));
        await repo.SetInitialPasswordAsync("alice", "original-hash", "original-salt");

        var result = await repo.SetInitialPasswordAsync("alice", "attacker-hash", "attacker-salt");

        Assert.Null(result);
        var stored = await repo.GetByUsernameAsync("alice");
        Assert.Equal("original-hash", stored!.PasswordHash);
        Assert.Equal("original-salt", stored.PasswordSalt);
    }

    [Fact]
    public async Task SetInitialPassword_UnknownAdmin_ReturnsNull()
    {
        var repo = CreateRepo();

        Assert.Null(await repo.SetInitialPasswordAsync("nobody", "hash", "salt"));
        Assert.Empty(await repo.GetAllAsync());
    }

    [Fact]
    public async Task AddRefreshToken_DeletedAdmin_ReturnsFalseAndDoesNotRecreate()
    {
        var repo = CreateRepo();
        await repo.CreateFirstSuperAdminAsync("root", "hash", "salt");
        await repo.AddAsync(Pending("alice"));
        await repo.DeleteAsync("alice");

        var stored = await repo.AddRefreshTokenAsync("alice",
            new RefreshTokenEntry { Token = "t", Expiry = DateTime.UtcNow.AddDays(1) }, revokeToken: null);

        Assert.False(stored);
        Assert.Null(await repo.GetByUsernameAsync("alice"));
    }

    [Fact]
    public async Task AddRefreshToken_RevokesGivenTokenAndPrunesExpired()
    {
        var repo = CreateRepo();
        await repo.CreateFirstSuperAdminAsync("root", "hash", "salt");
        await repo.AddRefreshTokenAsync("root", new() { Token = "expired", Expiry = DateTime.UtcNow.AddMinutes(-1) }, null);
        await repo.AddRefreshTokenAsync("root", new() { Token = "old", Expiry = DateTime.UtcNow.AddDays(1) }, null);
        await repo.AddRefreshTokenAsync("root", new() { Token = "other-device", Expiry = DateTime.UtcNow.AddDays(1) }, null);

        await repo.AddRefreshTokenAsync("root", new() { Token = "new", Expiry = DateTime.UtcNow.AddDays(1) }, revokeToken: "old");

        var tokens = (await repo.GetByUsernameAsync("root"))!.RefreshTokens.Select(t => t.Token).Order().ToArray();
        Assert.Equal(new[] { "new", "other-device" }, tokens);
    }

    [Fact]
    public async Task Delete_ExistingAdmin_RemovesIt()
    {
        var repo = CreateRepo();
        await repo.AddAsync(Pending("alice"));
        await repo.AddAsync(Pending("bob"));

        Assert.True(await repo.DeleteAsync("ALICE"));

        Assert.Equal("bob", Assert.Single(await repo.GetAllAsync()).Username);
    }

    [Fact]
    public async Task Delete_UnknownAdmin_ReturnsFalse()
    {
        var repo = CreateRepo();
        await repo.AddAsync(Pending("alice"));

        Assert.False(await repo.DeleteAsync("nobody"));
        Assert.Single(await repo.GetAllAsync());
    }

    [Fact]
    public async Task EnsureSuperAdmin_LegacyFileWithoutRole_PromotesFirstAdmin()
    {
        WriteRawFile("""
            [
              { "username": "legacy", "password_hash": "h", "password_salt": "s", "refresh_tokens": [] },
              { "username": "second", "password_hash": "h", "password_salt": "s", "refresh_tokens": [] }
            ]
            """);
        var repo = CreateRepo();

        var promoted = await repo.EnsureSuperAdminAsync();

        Assert.Equal("legacy", promoted);
        var admins = await repo.GetAllAsync();
        Assert.Equal(AdminRole.SuperAdmin, admins[0].Role);
        Assert.Equal(AdminRole.Admin, admins[1].Role);
    }

    [Fact]
    public async Task EnsureSuperAdmin_SuperAdminExists_NoChange()
    {
        var repo = CreateRepo();
        await repo.AddAsync(Pending("alice"));
        await repo.AddAsync(new Admin { Username = "root", Role = AdminRole.SuperAdmin, PasswordHash = "h", PasswordSalt = "s" });

        var promoted = await repo.EnsureSuperAdminAsync();

        Assert.Null(promoted);
        Assert.Equal(AdminRole.Admin, (await repo.GetByUsernameAsync("alice"))!.Role);
    }

    [Fact]
    public async Task EnsureSuperAdmin_EmptyStore_NoOp()
    {
        var repo = CreateRepo();

        Assert.Null(await repo.EnsureSuperAdminAsync());
        Assert.False(File.Exists(AdminsPath));
    }

    [Fact]
    public async Task Serialization_RoleAsSnakeCaseString_PendingPasswordAsNull()
    {
        var repo = CreateRepo();
        await repo.CreateFirstSuperAdminAsync("root", "hash", "salt");
        await repo.AddAsync(Pending("alice"));

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(AdminsPath));
        var root = doc.RootElement[0];
        var alice = doc.RootElement[1];

        Assert.Equal("super_admin", root.GetProperty("role").GetString());
        Assert.Equal("admin", alice.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, alice.GetProperty("password_hash").ValueKind);
        Assert.Equal(JsonValueKind.Null, alice.GetProperty("password_salt").ValueKind);
        Assert.False(alice.TryGetProperty("is_pending", out _));
    }

    [Fact]
    public async Task Save_LeavesNoTempFileBehind()
    {
        var repo = CreateRepo();

        await repo.AddAsync(Pending("alice"));

        Assert.Equal(new[] { "admins.json" }, Directory.GetFiles(_tempDir).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public async Task ConcurrentReadsDuringWrites_NeverFail()
    {
        var repo = CreateRepo();
        await repo.CreateFirstSuperAdminAsync("root", "hash", "salt");

        var writes = Enumerable.Range(0, 20).Select(i => Task.Run(() => repo.AddAsync(Pending($"admin{i}"))));
        var reads = Enumerable.Range(0, 50).Select(_ => Task.Run(() => repo.GetByUsernameAsync("root")));
        await Task.WhenAll(writes.Cast<Task>().Concat(reads));

        Assert.Equal(21, (await repo.GetAllAsync()).Count);
    }
}
