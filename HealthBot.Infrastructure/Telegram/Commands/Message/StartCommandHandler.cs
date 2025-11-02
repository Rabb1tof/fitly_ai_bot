using System;
using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Workflows;
using Microsoft.Extensions.Logging;
using TelegramMessage = Telegram.Bot.Types.Message;

namespace HealthBot.Infrastructure.Telegram.Commands.Message;

public sealed class StartCommandHandler : MessageCommandHandlerBase
{
    public StartCommandHandler(ILogger<StartCommandHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => -100;

    protected override bool CanHandle(TelegramMessage message, ConversationContext session)
    {
        if (string.IsNullOrWhiteSpace(message.Text))
        {
            return false;
        }

        var text = message.Text.Trim();
        return text.Equals("/start", StringComparison.OrdinalIgnoreCase)
               || text.Equals("/menu", StringComparison.OrdinalIgnoreCase);
    }

    protected override Task HandleAsync(CommandContext context, TelegramMessage message)
    {
        context.Session.Reset();
        var text = message.Text!.Trim();
        var intro = text.Equals("/start", StringComparison.OrdinalIgnoreCase)
            ? "Привет! Я Fitly.AI 🩺\n\nВыбери раздел ниже."
            : null;

        return MenuWorkflow.SendMainMenuAsync(context, intro);
    }
}
