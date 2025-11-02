using TelegramMessage = Telegram.Bot.Types.Message;

namespace HealthBot.Infrastructure.Telegram.Commands.Abstractions;

public interface IMessageCommandHandler : ITelegramCommandHandler
{
    bool CanHandle(TelegramMessage message, ConversationContext session);
}
