using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Workflows;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace HealthBot.Infrastructure.Telegram.Commands.Callback;

public sealed class MenuCallbackHandler : CallbackCommandHandlerBase
{
    public MenuCallbackHandler(ILogger<MenuCallbackHandler> logger)
        : base(logger)
    {
    }

    public override int Priority => -100;

    protected override bool CanHandle(CallbackQuery callbackQuery, ConversationContext session)
        => callbackQuery.Data == TelegramCommandNames.CallbackMenu;

    protected override Task HandleCallbackAsync(CommandContext context, CallbackQuery callbackQuery)
    {
        context.Session.Reset();
        return MenuWorkflow.SendMainMenuAsync(context);
    }
}
