using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using HealthBot.Shared.Options;
using Microsoft.Extensions.Options;

namespace HealthBot.Infrastructure.Telegram;

public sealed class InMemoryConversationContextStore : IConversationContextStore, IDisposable
{
    private sealed class SessionEntry
    {
        public SessionEntry(ConversationContext context, DateTimeOffset expiresAt)
        {
            Context = context;
            ExpiresAt = expiresAt;
        }

        public ConversationContext Context;
        public DateTimeOffset ExpiresAt;

        public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

        public void Refresh(DateTimeOffset now, TimeSpan ttl) => ExpiresAt = now.Add(ttl);
    }

    private readonly ConcurrentDictionary<long, SessionEntry> _sessions = new();
    private readonly TimeSpan _sessionTtl;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);
    private long _nextCleanupTicks;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public InMemoryConversationContextStore(IOptions<RedisOptions> options)
    {
        _sessionTtl = options?.Value?.GetDefaultTtl() ?? TimeSpan.FromMinutes(30);
        var nextCleanup = DateTimeOffset.UtcNow.Add(_cleanupInterval).Ticks;
        Interlocked.Exchange(ref _nextCleanupTicks, nextCleanup);
        _cleanupTimer = new Timer(_ => CleanupExpiredSessions(), null, _cleanupInterval, _cleanupInterval);
    }

    public Task<ConversationContext> GetSessionAsync(long chatId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        MaybeCleanup(now);

        var entry = _sessions.AddOrUpdate(
            chatId,
            _ => new SessionEntry(new ConversationContext(), now.Add(_sessionTtl)),
            (_, existing) => existing.IsExpired(now)
                ? new SessionEntry(new ConversationContext(), now.Add(_sessionTtl))
                : Refresh(existing, now));

        return Task.FromResult(entry.Context);
    }

    public Task SaveSessionAsync(long chatId, ConversationContext session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        MaybeCleanup(now);

        _sessions.AddOrUpdate(
            chatId,
            _ => new SessionEntry(session, now.Add(_sessionTtl)),
            (_, existing) => Update(existing, session, now));

        return Task.CompletedTask;
    }

    public Task DeleteSessionAsync(long chatId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessions.TryRemove(chatId, out _);
        return Task.CompletedTask;
    }

    private SessionEntry Refresh(SessionEntry entry, DateTimeOffset now)
    {
        entry.Refresh(now, _sessionTtl);
        return entry;
    }

    private SessionEntry Update(SessionEntry entry, ConversationContext session, DateTimeOffset now)
    {
        if (!ReferenceEquals(entry.Context, session))
        {
            entry.Context = session;
        }

        entry.Refresh(now, _sessionTtl);
        return entry;
    }

    private void MaybeCleanup(DateTimeOffset now)
    {
        var current = Interlocked.Read(ref _nextCleanupTicks);
        if (now.Ticks < current)
        {
            return;
        }

        var next = now.Add(_cleanupInterval).Ticks;
        if (Interlocked.CompareExchange(ref _nextCleanupTicks, next, current) != current)
        {
            return;
        }

        CleanupExpiredSessions(now);
    }

    private void CleanupExpiredSessions()
    {
        var now = DateTimeOffset.UtcNow;
        CleanupExpiredSessions(now);
    }

    private void CleanupExpiredSessions(DateTimeOffset now)
    {
        if (_disposed)
        {
            return;
        }

        foreach (var pair in _sessions)
        {
            if (pair.Value.IsExpired(now))
            {
                _sessions.TryRemove(pair.Key, out _);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cleanupTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}
