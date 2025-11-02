using System;
using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace HealthBot.Infrastructure.Telegram.Commands;

public abstract class CommandHandlerBase : ITelegramCommandHandler
{
    protected CommandHandlerBase(ILogger logger)
    {
        Logger = logger;
    }

    protected ILogger Logger { get; }

    public abstract int Priority { get; }

    public abstract bool CanHandle(Update update, ConversationContext session);

    public abstract Task HandleAsync(CommandContext context);

    protected T GetRequiredService<T>(CommandContext context) where T : notnull
        => context.Services.GetRequiredService<T>();
}
