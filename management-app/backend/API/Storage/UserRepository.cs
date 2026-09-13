using System.Text.Json;
using API.Models;

namespace API.Storage;

public sealed class UserRepository(IConfiguration config)
{
    private readonly string _path = config["Storage:UsersPath"] ?? "/data/users.json";
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<List<User>> GetAllAsync()
    {
        if (!File.Exists(_path)) return [];
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<User>>(stream, _json) ?? [];
    }

    public async Task SaveAllAsync(List<User> users)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, users, _json);
    }

    public async Task<User?> GetByIdAsync(string id)
    {
        var users = await GetAllAsync();
        return users.FirstOrDefault(u => u.Id == id);
    }

    public async Task<User> AddAsync(User user)
    {
        await _lock.WaitAsync();
        try
        {
            var users = await GetAllAsync();
            users.Add(user);
            await SaveAllAsync(users);
            return user;
        }
        finally { _lock.Release(); }
    }

    public async Task<User?> UpdateAsync(User updated)
    {
        await _lock.WaitAsync();
        try
        {
            var users = await GetAllAsync();
            var idx = users.FindIndex(u => u.Id == updated.Id);
            if (idx < 0) return null;
            users[idx] = updated;
            await SaveAllAsync(users);
            return updated;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            var users = await GetAllAsync();
            var removed = users.RemoveAll(u => u.Id == id);
            if (removed == 0) return false;
            await SaveAllAsync(users);
            return true;
        }
        finally { _lock.Release(); }
    }
}
