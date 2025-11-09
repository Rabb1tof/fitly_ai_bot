using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Workflows;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace HealthBot.Infrastructure.Telegram.Commands.Callback;

public sealed class SettingsQuietHoursEditCallbackHandler : CallbackCommandHandlerBase
{
    public SettingsQuietHoursEditCallbackHandler(ILogger<SettingsQuietHoursEditCallbackHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => -30;

    protected override bool CanHandle(CallbackQuery callbackQuery, ConversationContext session)
        => callbackQuery.Data == TelegramCommandNames.CallbackSettingsQuietHoursEdit;

    protected override Task HandleCallbackAsync(CommandContext context, CallbackQuery callbackQuery)
        => SettingsWorkflow.StartQuietHoursEditAsync(context);
}
