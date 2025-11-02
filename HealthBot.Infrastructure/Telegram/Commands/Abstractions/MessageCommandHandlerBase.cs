using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramMessage = Telegram.Bot.Types.Message;

namespace HealthBot.Infrastructure.Telegram.Commands.Abstractions;

public abstract class MessageCommandHandlerBase : CommandHandlerBase
{
    protected MessageCommandHandlerBase(ILogger logger) : base(logger)
    {
    }

    protected abstract bool CanHandle(TelegramMessage message, ConversationContext session);

    protected abstract Task HandleAsync(CommandContext context, TelegramMessage message);

    public override bool CanHandle(Update update, ConversationContext session)
        => update.Type == UpdateType.Message && update.Message is { } message && CanHandle(message, session);

    public override Task HandleAsync(CommandContext context)
        => HandleAsync(context, context.Message!);
}
