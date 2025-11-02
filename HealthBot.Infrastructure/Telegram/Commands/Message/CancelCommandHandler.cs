using System;
using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using Microsoft.Extensions.Logging;
using TelegramMessage = Telegram.Bot.Types.Message;

namespace HealthBot.Infrastructure.Telegram.Commands.Message;

public sealed class CancelCommandHandler : MessageCommandHandlerBase
{
    public CancelCommandHandler(ILogger<CancelCommandHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => -90;

    protected override bool CanHandle(TelegramMessage message, ConversationContext session)
    {
        if (string.IsNullOrWhiteSpace(message.Text))
        {
            return false;
        }

        return message.Text.Trim().Equals("/cancel", StringComparison.OrdinalIgnoreCase);
    }

    protected override async Task HandleAsync(CommandContext context, TelegramMessage message)
    {
        context.Session.Reset();
        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync("Диалог сброшен. Используй /menu, чтобы начать заново.");
    }
}
