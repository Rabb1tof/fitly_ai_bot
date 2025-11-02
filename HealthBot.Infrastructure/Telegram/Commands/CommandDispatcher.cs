using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using Microsoft.Extensions.Logging;

namespace HealthBot.Infrastructure.Telegram.Commands;

public class CommandDispatcher
{
    private readonly IReadOnlyList<ITelegramCommandHandler> _handlers;
    private readonly ILogger<CommandDispatcher> _logger;

    public CommandDispatcher(IEnumerable<ITelegramCommandHandler> handlers, ILogger<CommandDispatcher> logger)
    {
        _handlers = handlers.OrderBy(h => h.Priority).ToList();
        _logger = logger;
    }

    public async Task<bool> DispatchAsync(CommandContext context)
    {
        foreach (var handler in _handlers)
        {
            if (handler.CanHandle(context.Update, context.Session))
            {
                _logger.LogDebug("Handling update {UpdateType} with {Handler}", context.Update.Type, handler.GetType().Name);
                await handler.HandleAsync(context);
                return true;
            }
        }

        _logger.LogDebug("No handler matched update {UpdateType}", context.Update.Type);
        return false;
    }
}
