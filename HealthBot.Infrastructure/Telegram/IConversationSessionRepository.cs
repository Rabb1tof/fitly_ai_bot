using System.Threading;
using System.Threading.Tasks;
using HealthBot.Core.Entities;

namespace HealthBot.Infrastructure.Telegram;

public interface IConversationSessionRepository
{
    Task<ConversationSession?> GetAsync(long chatId, CancellationToken cancellationToken = default);
    Task UpsertAsync(ConversationSession session, CancellationToken cancellationToken = default);
    Task DeleteAsync(long chatId, CancellationToken cancellationToken = default);
}
