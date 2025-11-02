using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Workflows;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace HealthBot.Infrastructure.Telegram.Commands.Callback;

public sealed class MainNutritionCallbackHandler : CallbackCommandHandlerBase
{
    public MainNutritionCallbackHandler(ILogger<MainNutritionCallbackHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => -80;

    protected override bool CanHandle(CallbackQuery callbackQuery, ConversationContext session)
        => callbackQuery.Data == TelegramCommandNames.CallbackMainNutrition;

    protected override Task HandleCallbackAsync(CommandContext context, CallbackQuery callbackQuery)
    {
        context.Session.Reset();
        return ReminderWorkflow.ShowNutritionStubAsync(context);
    }
}
