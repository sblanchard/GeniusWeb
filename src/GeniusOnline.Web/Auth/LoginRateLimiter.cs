using System.Collections.Concurrent;

namespace GeniusOnline.Web.Auth;

public sealed class LoginRateLimiter
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
    private readonly TimeSpan _window = TimeSpan.FromMinutes(1);
    private const int MaxAttemptsPerWindow = 5;

    public bool TryRegister(string ip)
    {
        var now = DateTimeOffset.UtcNow;
        var bucket = _buckets.AddOrUpdate(ip,
            _ => new Bucket(now, 1),
            (_, current) => current.WindowStart + _window < now
                ? new Bucket(now, 1)
                : current with { Count = current.Count + 1 });

        return bucket.Count <= MaxAttemptsPerWindow;
    }

    public void Reset(string ip) => _buckets.TryRemove(ip, out _);

    private sealed record Bucket(DateTimeOffset WindowStart, int Count);
}
