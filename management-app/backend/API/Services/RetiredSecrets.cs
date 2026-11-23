using System.Collections.Concurrent;

namespace API.Services;

/// <summary>
/// Remembers secrets that were rotated away (single-connection users) so traffic Xray still has
/// counted under the old secret can be attributed to the right user by the next metrics collection.
/// In memory only: a restart forgets them, losing at most one collection period for rotated users.
/// </summary>
public sealed class RetiredSecrets
{
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (string UserId, DateTime RetiredAt)> _secrets = new();

    public void Retire(string secret, string userId, DateTime? now = null) =>
        _secrets[secret] = (userId, now ?? DateTime.UtcNow);

    /// <summary>Secret → user id for every retired secret still being tracked.</summary>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        _secrets.ToDictionary(kv => kv.Key, kv => kv.Value.UserId);

    /// <summary>
    /// Forgets secrets retired at least <see cref="Retention"/> ago. Call after a collection has read them:
    /// anything younger survives to the next collection, so every retired secret is read at least once afterwards.
    /// </summary>
    public void Purge(DateTime? now = null)
    {
        var threshold = (now ?? DateTime.UtcNow) - Retention;
        foreach (var (secret, entry) in _secrets)
            if (entry.RetiredAt <= threshold)
                _secrets.TryRemove(secret, out _);
    }
}
