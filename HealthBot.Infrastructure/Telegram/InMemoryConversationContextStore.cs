using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace HealthBot.Infrastructure.Telegram;

public sealed class InMemoryConversationContextStore : IConversationContextStore
{
    private readonly ConcurrentDictionary<long, ConversationContext> _sessions = new();

    public Task<ConversationContext> GetSessionAsync(long chatId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = _sessions.GetOrAdd(chatId, _ => new ConversationContext());
        return Task.FromResult(session);
    }

    public Task SaveSessionAsync(long chatId, ConversationContext session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessions[chatId] = session;
        return Task.CompletedTask;
    }

    public Task DeleteSessionAsync(long chatId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessions.TryRemove(chatId, out _);
        return Task.CompletedTask;
    }
}
