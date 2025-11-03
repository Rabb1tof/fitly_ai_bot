using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HealthBot.Infrastructure.Data;

public sealed class NoOpRedisCacheService : IRedisCacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        => Task.FromResult<T?>(default);

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<long> IncrementAsync(string key, long value = 1, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        => Task.FromResult(value);

    public Task<bool> AcquireLockAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> ReleaseLockAsync(string key, string value, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task AddToSortedSetAsync(string key, string member, double score, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<(string Member, double Score)>> RangeByScoreAsync(string key, double minScore, double maxScore, int count, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<(string Member, double Score)>>(Array.Empty<(string, double)>());

    public Task<long> RemoveRangeByScoreAsync(string key, double minScore, double maxScore, CancellationToken cancellationToken = default)
        => Task.FromResult(0L);

    public Task<bool> RemoveFromSortedSetAsync(string key, string member, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<long> RemoveFromSortedSetAsync(string key, IEnumerable<string> members, CancellationToken cancellationToken = default)
        => Task.FromResult(0L);
}
