using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types.Enums;

namespace HealthBot.Infrastructure.Telegram.Commands.Workflows;

public static class MenuWorkflow
{
    public static async Task SendMainMenuAsync(CommandContext context, string? messageText = null)
    {
        await context.DeleteLastMessageAsync();
        var text = messageText ?? "Выбери раздел:";
        await context.SendMessageAsync(text, Helpers.KeyboardFactory.MainMenu());
    }
}
