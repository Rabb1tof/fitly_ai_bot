using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Workflows;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace HealthBot.Infrastructure.Telegram.Commands.Callback;

public sealed class MainSettingsCallbackHandler : CallbackCommandHandlerBase
{
    public MainSettingsCallbackHandler(ILogger<MainSettingsCallbackHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => -70;

    protected override bool CanHandle(CallbackQuery callbackQuery, ConversationContext session)
        => callbackQuery.Data == TelegramCommandNames.CallbackMainSettings;

    protected override Task HandleCallbackAsync(CommandContext context, CallbackQuery callbackQuery)
    {
        context.Session.Reset();
        return SettingsWorkflow.ShowSettingsMenuAsync(context);
    }
}
