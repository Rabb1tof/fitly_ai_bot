using System.Threading;
using System.Threading.Tasks;
using HealthBot.Infrastructure.Data;
using HealthBot.Shared.Options;
using Microsoft.Extensions.Options;
using HealthBot.Core.Entities;
using System;
using Microsoft.Extensions.DependencyInjection;

namespace HealthBot.Infrastructure.Telegram;

public sealed class RedisConversationContextStore : IConversationContextStore
{
    private const string SessionKeyPrefix = "session:";

    private readonly IRedisCacheService _cache;
    private readonly RedisOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;

    public RedisConversationContextStore(
        IRedisCacheService cache,
        IServiceScopeFactory scopeFactory,
        IOptions<RedisOptions> options)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    public async Task<ConversationContext> GetSessionAsync(long chatId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = BuildKey(chatId);
        var cached = await _cache.GetAsync<ConversationContext>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IConversationSessionRepository>();
        var persisted = await repository.GetAsync(chatId, cancellationToken).ConfigureAwait(false);
        if (persisted is null)
        {
            return new ConversationContext();
        }

        var context = MapToContext(persisted);
        await CacheAsync(chatId, context, cancellationToken).ConfigureAwait(false);
        return context;
    }

    public async Task SaveSessionAsync(long chatId, ConversationContext session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = BuildKey(chatId);
        var entity = MapToEntity(chatId, session);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IConversationSessionRepository>();
        await repository.UpsertAsync(entity, cancellationToken).ConfigureAwait(false);
        await CacheAsync(chatId, session, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteSessionAsync(long chatId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = BuildKey(chatId);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IConversationSessionRepository>();
        await repository.DeleteAsync(chatId, cancellationToken).ConfigureAwait(false);
        await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildKey(long chatId) => SessionKeyPrefix + chatId;

    private Task CacheAsync(long chatId, ConversationContext context, CancellationToken cancellationToken)
        => _cache.SetAsync(BuildKey(chatId), context, _options.GetConversationSessionTtl(), cancellationToken);

    private static ConversationContext MapToContext(ConversationSession session)
        => new()
        {
            Flow = (ConversationFlow)session.Flow,
            Stage = (ConversationStage)session.Stage,
            TemplateCode = session.TemplateCode,
            TemplateId = session.TemplateId,
            TemplateTitle = session.TemplateTitle,
            TemplateDefaultRepeat = session.TemplateDefaultRepeat,
            CustomMessage = session.CustomMessage,
            FirstDelayMinutes = session.FirstDelayMinutes,
            ExpectManualInput = session.ExpectManualInput,
            LastBotMessageId = session.LastBotMessageId
        };

    private static ConversationSession MapToEntity(long chatId, ConversationContext context)
        => new()
        {
            ChatId = chatId,
            Flow = (int)context.Flow,
            Stage = (int)context.Stage,
            TemplateCode = context.TemplateCode,
            TemplateId = context.TemplateId,
            TemplateTitle = context.TemplateTitle,
            TemplateDefaultRepeat = context.TemplateDefaultRepeat,
            CustomMessage = context.CustomMessage,
            FirstDelayMinutes = context.FirstDelayMinutes,
            ExpectManualInput = context.ExpectManualInput,
            LastBotMessageId = context.LastBotMessageId
        };
}

