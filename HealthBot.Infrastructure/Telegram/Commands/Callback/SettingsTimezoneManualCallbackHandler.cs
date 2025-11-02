using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Workflows;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace HealthBot.Infrastructure.Telegram.Commands.Callback;

public sealed class SettingsTimezoneManualCallbackHandler : CallbackCommandHandlerBase
{
    public SettingsTimezoneManualCallbackHandler(ILogger<SettingsTimezoneManualCallbackHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => -32;

    protected override bool CanHandle(CallbackQuery callbackQuery, ConversationContext session)
        => callbackQuery.Data == TelegramCommandNames.CallbackSettingsTimezoneManual;

    protected override Task HandleCallbackAsync(CommandContext context, CallbackQuery callbackQuery)
        => SettingsWorkflow.StartManualTimezoneInputAsync(context);
}
