using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Workflows;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace HealthBot.Infrastructure.Telegram.Commands.Callback;

public sealed class SettingsTimezoneCallbackHandler : CallbackCommandHandlerBase
{
    public SettingsTimezoneCallbackHandler(ILogger<SettingsTimezoneCallbackHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => -34;

    protected override bool CanHandle(CallbackQuery callbackQuery, ConversationContext session)
        => callbackQuery.Data == TelegramCommandNames.CallbackSettingsTimezone;

    protected override Task HandleCallbackAsync(CommandContext context, CallbackQuery callbackQuery)
        => SettingsWorkflow.ShowTimezoneMenuAsync(context);
}
