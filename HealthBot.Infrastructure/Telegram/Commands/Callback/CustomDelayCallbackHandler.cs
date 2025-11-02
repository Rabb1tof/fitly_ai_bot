using System;
using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Workflows;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace HealthBot.Infrastructure.Telegram.Commands.Callback;

public sealed class CustomDelayCallbackHandler : CallbackCommandHandlerBase
{
    public CustomDelayCallbackHandler(ILogger<CustomDelayCallbackHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => -42;

    protected override bool CanHandle(CallbackQuery callbackQuery, ConversationContext session)
    {
        var data = callbackQuery.Data;
        return data is not null && data.StartsWith(TelegramCommandNames.CallbackCustomDelay, StringComparison.Ordinal);
    }

    protected override Task HandleCallbackAsync(CommandContext context, CallbackQuery callbackQuery)
    {
        var parts = SplitCallbackData(callbackQuery);
        return ReminderWorkflow.HandleCustomDelayCallbackAsync(context, parts);
    }
}
