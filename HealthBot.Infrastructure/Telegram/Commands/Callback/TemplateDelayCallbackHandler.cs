using System;
using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Workflows;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace HealthBot.Infrastructure.Telegram.Commands.Callback;

public sealed class TemplateDelayCallbackHandler : CallbackCommandHandlerBase
{
    public TemplateDelayCallbackHandler(ILogger<TemplateDelayCallbackHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => -45;

    protected override bool CanHandle(CallbackQuery callbackQuery, ConversationContext session)
    {
        var data = callbackQuery.Data;
        return data is not null && data.StartsWith(TelegramCommandNames.CallbackTemplateDelay, StringComparison.Ordinal);
    }

    protected override Task HandleCallbackAsync(CommandContext context, CallbackQuery callbackQuery)
    {
        var parts = SplitCallbackData(callbackQuery);
        return ReminderWorkflow.HandleTemplateDelayAsync(context, parts);
    }
}
