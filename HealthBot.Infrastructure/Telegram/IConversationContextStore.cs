using System.Threading;
using System.Threading.Tasks;

namespace HealthBot.Infrastructure.Telegram;

public interface IConversationContextStore
{
    Task<ConversationContext> GetSessionAsync(long chatId, CancellationToken cancellationToken = default);

    Task SaveSessionAsync(long chatId, ConversationContext session, CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(long chatId, CancellationToken cancellationToken = default);
}
