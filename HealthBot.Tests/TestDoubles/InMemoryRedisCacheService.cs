using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthBot.Infrastructure.Data;

namespace HealthBot.Tests.TestDoubles;

public sealed class InMemoryRedisCacheService : IRedisCacheService
{
    private sealed class CacheEntry
    {
        public object? Value { get; init; }
        public DateTime? Expiry { get; init; }

        public bool IsExpired(DateTime utcNow)
            => Expiry is { } expiry && expiry <= utcNow;
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _values = new();
    private readonly Dictionary<string, CacheEntry> _locks = new();
    private readonly Dictionary<string, Dictionary<string, double>> _sortedSets = new();

    private readonly Dictionary<string, Exception> _oneShotFaults = new();
    private readonly Dictionary<string, Func<Exception>> _repeatFaults = new();

    private static DateTime UtcNow => DateTime.UtcNow;

    public void ConfigureOneShotFault(string key, Exception exception)
    {
        lock (_sync)
        {
            _oneShotFaults[key] = exception;
        }
    }

    public void ConfigureRepeatFault(string key, Func<Exception> factory)
    {
        lock (_sync)
        {
            _repeatFaults[key] = factory;
        }
    }

    private void ThrowIfFaulted(string key)
    {
        if (_oneShotFaults.TryGetValue(key, out var oneShot))
        {
            _oneShotFaults.Remove(key);
            throw oneShot;
        }

        if (_repeatFaults.TryGetValue(key, out var factory))
        {
            throw factory();
        }
    }

    private void PurgeExpiredEntries()
    {
        var now = UtcNow;
        var expiredKeys = _values.Where(pair => pair.Value.IsExpired(now)).Select(pair => pair.Key).ToList();
        foreach (var key in expiredKeys)
        {
            _values.Remove(key);
        }

        var expiredLocks = _locks.Where(pair => pair.Value.IsExpired(now)).Select(pair => pair.Key).ToList();
        foreach (var key in expiredLocks)
        {
            _locks.Remove(key);
        }
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfFaulted(key);
            PurgeExpiredEntries();
            if (!_values.TryGetValue(key, out var entry) || entry.IsExpired(UtcNow))
            {
                _values.Remove(key);
                return Task.FromResult<T?>(default);
            }

            if (entry.Value is T typed)
            {
                return Task.FromResult<T?>(typed);
            }

            return Task.FromResult<T?>(default);
        }
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfFaulted(key);
            _values[key] = new CacheEntry
            {
                Value = value,
                Expiry = ttl is { } span && span > TimeSpan.Zero ? UtcNow.Add(span) : null
            };
        }

        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfFaulted(key);
            return Task.FromResult(_values.Remove(key));
        }
    }

    public Task<long> IncrementAsync(string key, long value = 1, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfFaulted(key);
            if (!_values.TryGetValue(key, out var entry) || entry.IsExpired(UtcNow))
            {
                entry = new CacheEntry { Value = 0L };
            }

            var current = entry.Value switch
            {
                long longValue => longValue,
                string stringValue when long.TryParse(stringValue, out var parsed) => parsed,
                _ => 0L
            };

            var updated = current + value;
            _values[key] = new CacheEntry
            {
                Value = updated,
                Expiry = ttl is { } span && span > TimeSpan.Zero ? UtcNow.Add(span) : entry.Expiry
            };

            return Task.FromResult(updated);
        }
    }

    public Task<bool> AcquireLockAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfFaulted(key);
            if (_locks.TryGetValue(key, out var existing) && !existing.IsExpired(UtcNow))
            {
                return Task.FromResult(false);
            }

            _locks[key] = new CacheEntry
            {
                Value = value,
                Expiry = ttl <= TimeSpan.Zero ? null : UtcNow.Add(ttl)
            };

            return Task.FromResult(true);
        }
    }

    public Task<bool> ReleaseLockAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfFaulted(key);
            if (_locks.TryGetValue(key, out var existing) && !existing.IsExpired(UtcNow) && Equals(existing.Value, value))
            {
                _locks.Remove(key);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
    }

    public Task AddToSortedSetAsync(string key, string member, double score, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfFaulted(key);
            if (!_sortedSets.TryGetValue(key, out var set))
            {
                set = new Dictionary<string, double>();
                _sortedSets[key] = set;
            }

            set[member] = score;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string Member, double Score)>> RangeByScoreAsync(
        string key,
        double minScore,
        double maxScore,
        int count,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfFaulted(key);
            if (!_sortedSets.TryGetValue(key, out var set))
            {
                return Task.FromResult<IReadOnlyList<(string Member, double Score)>>(Array.Empty<(string, double)>());
            }

            var query = set
                .Where(pair => pair.Value >= minScore && pair.Value <= maxScore)
                .OrderBy(pair => pair.Value)
                .Take(count > 0 ? count : int.MaxValue)
                .Select(pair => (pair.Key, pair.Value))
                .ToList();

            return Task.FromResult<IReadOnlyList<(string Member, double Score)>>(query);
        }
    }

    public Task<long> RemoveRangeByScoreAsync(string key, double minScore, double maxScore, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfFaulted(key);
            if (!_sortedSets.TryGetValue(key, out var set))
            {
                return Task.FromResult(0L);
            }

            var members = set
                .Where(pair => pair.Value >= minScore && pair.Value <= maxScore)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var member in members)
            {
                set.Remove(member);
            }

            return Task.FromResult((long)members.Count);
        }
    }

    public Task<bool> RemoveFromSortedSetAsync(string key, string member, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfFaulted(key);
            return Task.FromResult(_sortedSets.TryGetValue(key, out var set) && set.Remove(member));
        }
    }

    public Task<long> RemoveFromSortedSetAsync(string key, IEnumerable<string> members, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfFaulted(key);
            if (!_sortedSets.TryGetValue(key, out var set))
            {
                return Task.FromResult(0L);
            }

            var removed = 0L;
            foreach (var member in members)
            {
                if (set.Remove(member))
                {
                    removed++;
                }
            }

            return Task.FromResult(removed);
        }
    }

    public bool TryGetRaw(string key, out object? value)
    {
        lock (_sync)
        {
            PurgeExpiredEntries();
            if (_values.TryGetValue(key, out var entry) && !entry.IsExpired(UtcNow))
            {
                value = entry.Value;
                return true;
            }

            value = null;
            return false;
        }
    }

    public IReadOnlyDictionary<string, double> GetSortedSetSnapshot(string key)
    {
        lock (_sync)
        {
            ThrowIfFaulted(key);
            if (_sortedSets.TryGetValue(key, out var set))
            {
                return new Dictionary<string, double>(set);
            }

            return new Dictionary<string, double>();
        }
    }
}
