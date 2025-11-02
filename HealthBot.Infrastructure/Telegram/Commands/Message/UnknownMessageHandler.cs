using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using Microsoft.Extensions.Logging;
using TelegramMessage = Telegram.Bot.Types.Message;

namespace HealthBot.Infrastructure.Telegram.Commands.Message;

public sealed class UnknownMessageHandler : MessageCommandHandlerBase
{
    public UnknownMessageHandler(ILogger<UnknownMessageHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => int.MaxValue;

    protected override bool CanHandle(TelegramMessage message, ConversationContext session)
        => true;

    protected override async Task HandleAsync(CommandContext context, TelegramMessage message)
    {
        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync("Я пока не понимаю это сообщение. Используй /menu для управления напоминаниями.");
    }
}
