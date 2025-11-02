using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Workflows;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace HealthBot.Infrastructure.Telegram.Commands.Callback;

public sealed class CustomNewCallbackHandler : CallbackCommandHandlerBase
{
    public CustomNewCallbackHandler(ILogger<CustomNewCallbackHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => -52;

    protected override bool CanHandle(CallbackQuery callbackQuery, ConversationContext session)
        => callbackQuery.Data == TelegramCommandNames.CallbackCustomNew;

    protected override Task HandleCallbackAsync(CommandContext context, CallbackQuery callbackQuery)
        => ReminderWorkflow.StartCustomFlowAsync(context);
}
