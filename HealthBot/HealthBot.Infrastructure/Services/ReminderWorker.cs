using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Microsoft.Extensions.Logging;

namespace HealthBot.Infrastructure.Services;

public class ReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderWorker> _logger;

    public ReminderWorker(IServiceScopeFactory scopeFactory, ILogger<ReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reminderService = scope.ServiceProvider.GetRequiredService<ReminderService>();
                var botClient = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();

                var now = DateTime.UtcNow;
                var dueReminders = await reminderService.GetDueRemindersAsync(now, stoppingToken);

                foreach (var reminder in dueReminders)
                {
                    if (reminder.User is null)
                    {
                        _logger.LogWarning("Reminder {ReminderId} has no associated user", reminder.Id);
                        continue;
                    }

                    await botClient.SendMessage(
                        new ChatId(reminder.User.TelegramId),
                        $"🔔 {reminder.Message}",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: stoppingToken);
                }

                if (dueReminders.Count > 0)
                {
                    await reminderService.MarkAsSentAsync(dueReminders, now, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error while processing reminders");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
