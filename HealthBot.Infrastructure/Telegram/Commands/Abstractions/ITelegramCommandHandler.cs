using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace HealthBot.Infrastructure.Telegram.Commands.Abstractions;

public interface ITelegramCommandHandler
{
    int Priority { get; }

    bool CanHandle(Update update, ConversationContext session);

    Task HandleAsync(CommandContext context);
}
