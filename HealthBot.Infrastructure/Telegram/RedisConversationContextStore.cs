using System;
using System.Threading;
using System.Threading.Tasks;
using HealthBot.Infrastructure.Data;
using HealthBot.Shared.Options;
using Microsoft.Extensions.Options;

namespace HealthBot.Infrastructure.Telegram;

public sealed class RedisConversationContextStore : IConversationContextStore
{
    private const string SessionKeyPrefix = "session:";

    private readonly IRedisCacheService _cache;
    private readonly RedisOptions _options;

    public RedisConversationContextStore(IRedisCacheService cache, IOptions<RedisOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    public async Task<ConversationContext> GetSessionAsync(long chatId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = BuildKey(chatId);
        var session = await _cache.GetAsync<ConversationContext>(key, cancellationToken).ConfigureAwait(false);
        return session ?? new ConversationContext();
    }

    public Task SaveSessionAsync(long chatId, ConversationContext session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = BuildKey(chatId);
        return _cache.SetAsync(key, session, _options.GetDefaultTtl(), cancellationToken);
    }

    public Task DeleteSessionAsync(long chatId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = BuildKey(chatId);
        return _cache.RemoveAsync(key, cancellationToken);
    }

    private static string BuildKey(long chatId) => SessionKeyPrefix + chatId;
}
