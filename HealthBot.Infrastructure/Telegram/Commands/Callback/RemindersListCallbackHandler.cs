using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Workflows;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace HealthBot.Infrastructure.Telegram.Commands.Callback;

public sealed class RemindersListCallbackHandler : CallbackCommandHandlerBase
{
    public RemindersListCallbackHandler(ILogger<RemindersListCallbackHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => -60;

    protected override bool CanHandle(CallbackQuery callbackQuery, ConversationContext session)
        => callbackQuery.Data == TelegramCommandNames.CallbackRemindersList;

    protected override Task HandleCallbackAsync(CommandContext context, CallbackQuery callbackQuery)
    {
        context.Session.ResetFlowState();
        return ReminderWorkflow.ShowReminderListAsync(context);
    }
}
