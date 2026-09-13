using System.Text.Json;
using API.Models;

namespace API.Storage;

public sealed class AdminRepository(IConfiguration config)
{
    private readonly string _path = config["Storage:AdminsPath"] ?? "/data/admins.json";
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<List<Admin>> GetAllAsync()
    {
        if (!File.Exists(_path)) return [];
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<Admin>>(stream, _json) ?? [];
    }

    public async Task<Admin?> GetByUsernameAsync(string username)
    {
        var admins = await GetAllAsync();
        return admins.FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpdateAsync(Admin updated)
    {
        var admins = await GetAllAsync();
        var idx = admins.FindIndex(a => string.Equals(a.Username, updated.Username, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) admins[idx] = updated;
        else admins.Add(updated);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, admins, _json);
    }
}
