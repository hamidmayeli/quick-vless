using Microsoft.Extensions.Configuration;
using API.Models;
using API.Storage;

namespace API.Tests.Unit;

public sealed class UserRepositoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "easy-xray-users-" + Guid.NewGuid());

    private string UsersPath => Path.Combine(_tempDir, "users.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private UserRepository CreateRepo() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:UsersPath"] = UsersPath })
            .Build());

    private static User UserCreatedBy(string id, string? createdBy) => new()
    {
        Id = id,
        Name = id,
        Secret = Guid.NewGuid().ToString(),
        CreatedBy = createdBy,
    };

    [Fact]
    public async Task ReassignOwner_MovesOnlyMatchingUsers_ReturnsCount()
    {
        var repo = CreateRepo();
        await repo.AddAsync(UserCreatedBy("a1", "alice"));
        await repo.AddAsync(UserCreatedBy("a2", "Alice"));
        await repo.AddAsync(UserCreatedBy("b1", "bob"));
        await repo.AddAsync(UserCreatedBy("l1", null));

        var moved = await repo.ReassignOwnerAsync("alice", "root");

        Assert.Equal(2, moved);
        var owners = (await repo.GetAllAsync()).ToDictionary(u => u.Id, u => u.CreatedBy);
        Assert.Equal("root", owners["a1"]);
        Assert.Equal("root", owners["a2"]);
        Assert.Equal("bob", owners["b1"]);
        Assert.Null(owners["l1"]);
    }

    [Fact]
    public async Task ReassignOwner_NoMatchingUsers_ReturnsZero()
    {
        var repo = CreateRepo();
        await repo.AddAsync(UserCreatedBy("b1", "bob"));

        Assert.Equal(0, await repo.ReassignOwnerAsync("alice", "root"));
        Assert.Equal("bob", (await repo.GetByIdAsync("b1"))!.CreatedBy);
    }

    [Fact]
    public async Task LegacyFileWithoutCreatedBy_DeserializesAsNull()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(UsersPath, """
            [{ "id": "legacy", "name": "Legacy", "secret": "s", "enabled": true }]
            """);

        var user = Assert.Single(await CreateRepo().GetAllAsync());

        Assert.Null(user.CreatedBy);
    }
}
