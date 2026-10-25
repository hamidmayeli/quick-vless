using System.Text.Json;

namespace API.Storage;

public sealed class AppSettings
{
    public string? XrayActiveProfile { get; set; }
    public string? DomainOverride { get; set; }
}

/// <summary>App-wide settings chosen in the UI, stored in settings.json.</summary>
public sealed class SettingsRepository(IConfiguration config)
{
    private readonly string _path = config["Storage:SettingsPath"] ?? "/data/settings.json";
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<AppSettings> GetAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await ReadAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAsync(Action<AppSettings> change)
    {
        await _lock.WaitAsync();
        try
        {
            var settings = await ReadAsync();
            change(settings);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var tempPath = _path + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, _json);
            }
            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<AppSettings> ReadAsync()
    {
        if (!File.Exists(_path)) return new AppSettings();
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, _json) ?? new AppSettings();
    }
}
