using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthBot.Shared.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace HealthBot.Infrastructure.Data;

public sealed class RedisCacheService : IRedisCacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly RedisOptions _options;
    private readonly ILogger<RedisCacheService> _logger;
    private IDatabase? _database;

    public RedisCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        IOptions<RedisOptions> options,
        ILogger<RedisCacheService> logger)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _options = options.Value;
        _logger = logger;
    }

    private IDatabase Database => _database ??= _connectionMultiplexer.GetDatabase();

    private string BuildKey(string key)
        => string.IsNullOrWhiteSpace(_options.KeyPrefix)
            ? key
            : string.Concat(_options.KeyPrefix, key);

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cachedValue = await Database.StringGetAsync(BuildKey(key)).ConfigureAwait(false);
        if (!cachedValue.HasValue)
        {
            return default;
        }

        if (typeof(T) == typeof(string))
        {
            return (T)(object)cachedValue.ToString();
        }

        return JsonSerializer.Deserialize<T>(cachedValue!, SerializerOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = value is string stringValue
            ? stringValue
            : JsonSerializer.Serialize(value, SerializerOptions);

        var expiry = ttl ?? _options.GetDefaultTtl();
        await Database.StringSetAsync(BuildKey(key), payload, expiry).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Database.KeyDeleteAsync(BuildKey(key)).ConfigureAwait(false);
    }

    public async Task<long> IncrementAsync(string key, long value = 1, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var redisKey = BuildKey(key);
        var result = await Database.StringIncrementAsync(redisKey, value).ConfigureAwait(false);

        if (ttl is { } expiration && expiration > TimeSpan.Zero)
        {
            await Database.KeyExpireAsync(redisKey, expiration).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<bool> AcquireLockAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Database.LockTakeAsync(BuildKey(key), value, ttl).ConfigureAwait(false);
    }

    public async Task<bool> ReleaseLockAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Database.LockReleaseAsync(BuildKey(key), value).ConfigureAwait(false);
    }

    public async Task AddToSortedSetAsync(string key, string member, double score, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Database.SortedSetAddAsync(BuildKey(key), member, score).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(string Member, double Score)>> RangeByScoreAsync(
        string key,
        double minScore,
        double maxScore,
        int count,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = await Database.SortedSetRangeByScoreWithScoresAsync(
                BuildKey(key),
                minScore,
                maxScore,
                take: count)
            .ConfigureAwait(false);

        var buffer = new List<(string Member, double Score)>(results.Length);
        foreach (var entry in results)
        {
            if (entry.Element.IsNullOrEmpty)
            {
                continue;
            }

            buffer.Add((entry.Element!, entry.Score));
        }

        return buffer;
    }

    public async Task<long> RemoveRangeByScoreAsync(string key, double minScore, double maxScore, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Database.SortedSetRemoveRangeByScoreAsync(BuildKey(key), minScore, maxScore).ConfigureAwait(false);
    }

}
