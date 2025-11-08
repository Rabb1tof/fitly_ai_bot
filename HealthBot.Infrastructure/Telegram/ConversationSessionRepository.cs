using System;
using System.Threading;
using System.Threading.Tasks;
using HealthBot.Core.Entities;
using HealthBot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace HealthBot.Infrastructure.Telegram;

public sealed class ConversationSessionRepository : IConversationSessionRepository
{
    private readonly HealthBotDbContext _dbContext;

    public ConversationSessionRepository(HealthBotDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ConversationSession?> GetAsync(long chatId, CancellationToken cancellationToken)
    {
        return await _dbContext.ConversationSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ChatId == chatId, cancellationToken);
    }

    public async Task UpsertAsync(ConversationSession session, CancellationToken cancellationToken)
    {
        session.UpdatedAt = DateTimeOffset.UtcNow;

        var existing = await _dbContext.ConversationSessions
            .FirstOrDefaultAsync(s => s.ChatId == session.ChatId, cancellationToken);

        if (existing is null)
        {
            await _dbContext.ConversationSessions.AddAsync(session, cancellationToken);
        }
        else
        {
            _dbContext.Entry(existing).CurrentValues.SetValues(session);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (existing is null)
        {
            _dbContext.Entry(session).State = EntityState.Detached;
        }
        else
        {
            _dbContext.Entry(existing).State = EntityState.Detached;
        }
    }

    public async Task DeleteAsync(long chatId, CancellationToken cancellationToken)
    {
        await _dbContext.ConversationSessions
            .Where(s => s.ChatId == chatId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
