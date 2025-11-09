using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Workflows;
using Microsoft.Extensions.Logging;
using TelegramMessage = Telegram.Bot.Types.Message;

namespace HealthBot.Infrastructure.Telegram.Commands.Message;

public sealed class ManualInputMessageHandler : MessageCommandHandlerBase
{
    public ManualInputMessageHandler(ILogger<ManualInputMessageHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => 0;

    protected override bool CanHandle(TelegramMessage message, ConversationContext session)
        => session.ExpectManualInput && !string.IsNullOrWhiteSpace(message.Text);

    protected override async Task HandleAsync(CommandContext context, TelegramMessage message)
    {
        var session = context.Session;
        var text = message.Text!.Trim();

        switch (session.Stage)
        {
            case ConversationStage.AwaitingCustomMessage:
                await ReminderWorkflow.HandleCustomMessageAsync(context, text);
                break;
            case ConversationStage.AwaitingFirstDelayMinutes when session.ExpectManualInput:
                await ReminderWorkflow.HandleManualDelayAsync(context, text);
                break;
            case ConversationStage.AwaitingRepeatMinutes when session.ExpectManualInput:
                await ReminderWorkflow.HandleManualRepeatAsync(context, text);
                break;
            case ConversationStage.AwaitingTimeZoneManual when session.ExpectManualInput:
                await SettingsWorkflow.HandleManualTimezoneAsync(context, text);
                break;
            case ConversationStage.AwaitingQuietHoursStart when session.ExpectManualInput:
                await SettingsWorkflow.HandleQuietHoursStartAsync(context, text);
                break;
            case ConversationStage.AwaitingQuietHoursEnd when session.ExpectManualInput:
                await SettingsWorkflow.HandleQuietHoursEndAsync(context, text);
                break;
            default:
                await context.SendMessageAsync("Я пока не понимаю это сообщение. Используй /menu для управления напоминаниями.");
                break;
        }
    }
}
