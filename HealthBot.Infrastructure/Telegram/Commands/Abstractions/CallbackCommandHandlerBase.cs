using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace HealthBot.Infrastructure.Telegram.Commands.Abstractions;

public abstract class CallbackCommandHandlerBase : CommandHandlerBase
{
    protected CallbackCommandHandlerBase(ILogger logger) : base(logger)
    {
    }

    protected abstract bool CanHandle(CallbackQuery callbackQuery, ConversationContext session);

    protected abstract Task HandleCallbackAsync(CommandContext context, CallbackQuery callbackQuery);

    public override bool CanHandle(Update update, ConversationContext session)
        => update.Type == UpdateType.CallbackQuery && update.CallbackQuery is { } callback && CanHandle(callback, session);

    public override async Task HandleAsync(CommandContext context)
    {
        await HandleCallbackAsync(context, context.CallbackQuery!);
        if (!context.CallbackAnswered)
        {
            await context.AnswerCallbackAsync();
        }
    }

    protected static string[] SplitCallbackData(CallbackQuery callbackQuery)
    {
        var data = callbackQuery.Data ?? string.Empty;
        return data.Split(':', StringSplitOptions.RemoveEmptyEntries);
    }
}
