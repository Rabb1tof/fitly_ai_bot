using System;
using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Workflows;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace HealthBot.Infrastructure.Telegram.Commands.Callback;

public sealed class TemplateSelectCallbackHandler : CallbackCommandHandlerBase
{
    public TemplateSelectCallbackHandler(ILogger<TemplateSelectCallbackHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => -50;

    protected override bool CanHandle(CallbackQuery callbackQuery, ConversationContext session)
    {
        var data = callbackQuery.Data;
        if (string.IsNullOrEmpty(data))
        {
            return false;
        }

        var prefix = data.Split(':', 2)[0];
        return string.Equals(prefix, TelegramCommandNames.CallbackTemplateSelect, StringComparison.Ordinal);
    }

    protected override Task HandleCallbackAsync(CommandContext context, CallbackQuery callbackQuery)
    {
        var parts = SplitCallbackData(callbackQuery);
        return ReminderWorkflow.HandleTemplateSelectedAsync(context, parts);
    }
}
