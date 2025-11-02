using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Workflows;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace HealthBot.Infrastructure.Telegram.Commands.Callback;

public sealed class RemindersTemplatesCallbackHandler : CallbackCommandHandlerBase
{
    public RemindersTemplatesCallbackHandler(ILogger<RemindersTemplatesCallbackHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => -55;

    protected override bool CanHandle(CallbackQuery callbackQuery, ConversationContext session)
        => callbackQuery.Data == TelegramCommandNames.CallbackRemindersTemplates;

    protected override Task HandleCallbackAsync(CommandContext context, CallbackQuery callbackQuery)
    {
        context.Session.Reset();
        return ReminderWorkflow.ShowReminderTemplatesAsync(context);
    }
}
