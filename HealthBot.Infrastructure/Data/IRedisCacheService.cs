using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HealthBot.Infrastructure.Data;

public interface IRedisCacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task<long> IncrementAsync(string key, long value = 1, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    Task<bool> AcquireLockAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task<bool> ReleaseLockAsync(string key, string value, CancellationToken cancellationToken = default);

    Task AddToSortedSetAsync(string key, string member, double score, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(string Member, double Score)>> RangeByScoreAsync(
        string key,
        double minScore,
        double maxScore,
        int count,
        CancellationToken cancellationToken = default);

    Task<long> RemoveRangeByScoreAsync(string key, double minScore, double maxScore, CancellationToken cancellationToken = default);
}
