using System.Text.Json;
using System.Text.Json.Serialization;
using API.Models;

namespace API.Storage;

public sealed class AdminRepository(IConfiguration config)
{
    private readonly string _path = config["Storage:AdminsPath"] ?? "/data/admins.json";
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };
    private static readonly SemaphoreSlim _lock = new(1, 1);

    // Reads take the lock too: every write replaces the file, and Windows refuses to replace an open file.
    public async Task<List<Admin>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await ReadAllAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Admin?> GetByUsernameAsync(string username)
    {
        var admins = await GetAllAsync();
        return Find(admins, username);
    }

    /// <summary>Creates the super admin, but only while no admin exists yet.</summary>
    public Task<Admin?> CreateFirstSuperAdminAsync(string username, string hash, string salt) =>
        MutateAsync<Admin?>(admins =>
        {
            if (admins.Count > 0) return (null, false);
            var admin = new Admin
            {
                Username = username,
                Role = AdminRole.SuperAdmin,
                PasswordHash = hash,
                PasswordSalt = salt,
            };
            admins.Add(admin);
            return (admin, true);
        });

    /// <summary>Adds a new admin. Returns false when the username is already taken.</summary>
    public Task<bool> AddAsync(Admin admin) =>
        MutateAsync(admins =>
        {
            if (Find(admins, admin.Username) is not null) return (false, false);
            admins.Add(admin);
            return (true, true);
        });

    /// <summary>Sets the password of a pending admin. Returns null when the admin is unknown or already has a password.</summary>
    public Task<Admin?> SetInitialPasswordAsync(string username, string hash, string salt) =>
        MutateAsync<Admin?>(admins =>
        {
            var admin = Find(admins, username);
            if (admin is null || !admin.IsPending) return (null, false);
            admin.PasswordHash = hash;
            admin.PasswordSalt = salt;
            return (admin, true);
        });

    /// <summary>Stores a refresh token, dropping expired ones and <paramref name="revokeToken"/>. Returns false when the admin no longer exists.</summary>
    public Task<bool> AddRefreshTokenAsync(string username, RefreshTokenEntry entry, string? revokeToken) =>
        MutateAsync(admins =>
        {
            var admin = Find(admins, username);
            if (admin is null) return (false, false);
            admin.RefreshTokens.RemoveAll(rt => rt.Expiry <= DateTime.UtcNow || rt.Token == revokeToken);
            admin.RefreshTokens.Add(entry);
            return (true, true);
        });

    public Task<bool> DeleteAsync(string username) =>
        MutateAsync(admins =>
        {
            var removed = admins.RemoveAll(a => IsSameUsername(a.Username, username)) > 0;
            return (removed, removed);
        });

    /// <summary>
    /// Promotes the first admin to super admin when admins exist but none is a super admin
    /// (admins.json written before roles existed). Returns the promoted username, if any.
    /// </summary>
    public Task<string?> EnsureSuperAdminAsync() =>
        MutateAsync<string?>(admins =>
        {
            if (admins.Count == 0 || admins.Any(a => a.Role == AdminRole.SuperAdmin)) return (null, false);
            admins[0].Role = AdminRole.SuperAdmin;
            return (admins[0].Username, true);
        });

    private async Task<T> MutateAsync<T>(Func<List<Admin>, (T Result, bool Changed)> mutate)
    {
        await _lock.WaitAsync();
        try
        {
            var admins = await ReadAllAsync();
            var (result, changed) = mutate(admins);
            if (changed) await SaveAllAsync(admins);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<Admin>> ReadAllAsync()
    {
        if (!File.Exists(_path)) return [];
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<Admin>>(stream, _json) ?? [];
    }

    // Writes to a temp file and swaps it in, so a crash mid-write never leaves a truncated admins.json.
    private async Task SaveAllAsync(List<Admin> admins)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tempPath = _path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, admins, _json);
        }
        File.Move(tempPath, _path, overwrite: true);
    }

    private static Admin? Find(List<Admin> admins, string username) =>
        admins.FirstOrDefault(a => IsSameUsername(a.Username, username));

    private static bool IsSameUsername(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
