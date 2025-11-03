using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using HealthBot.Shared.Options;

namespace HealthBot.Infrastructure.Services;

public class ReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderWorker> _logger;
    private readonly RedisOptions _redisOptions;
    private readonly TimeSpan _idleDelay;

    public ReminderWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ReminderWorker> logger,
        IOptions<RedisOptions> redisOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _redisOptions = redisOptions.Value;
        _idleDelay = _redisOptions.GetReminderWorkerPollInterval();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var nextDelay = _idleDelay;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reminderService = scope.ServiceProvider.GetRequiredService<ReminderService>();
                var botClient = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();

                var now = DateTime.UtcNow;
                var lookaheadMinutes = Math.Max(1, _redisOptions.ReminderLookaheadMinutes);
                var dueUntil = now.AddMinutes(lookaheadMinutes);

                var leases = await reminderService.DequeueDueRemindersAsync(now, dueUntil, stoppingToken);
                if (leases.Count == 0)
                {
                    await Task.Delay(_idleDelay, stoppingToken);
                    continue;
                }

                var delivered = new List<ReminderService.ReminderLease>(leases.Count);
                var releaseQueue = new List<(Guid ReminderId, string LockValue)>(leases.Count);

                foreach (var lease in leases)
                {
                    var reminder = lease.Reminder;
                    releaseQueue.Add((reminder.Id, lease.LockValue));

                    if (reminder.User is null)
                    {
                        _logger.LogWarning("Reminder {ReminderId} has no associated user", reminder.Id);
                        continue;
                    }

                    try
                    {
                        await botClient.SendMessage(
                            new ChatId(reminder.User.TelegramId),
                            $"🔔 {reminder.Message}",
                            parseMode: ParseMode.Markdown,
                            cancellationToken: stoppingToken);

                        delivered.Add(lease);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Failed to deliver reminder {ReminderId} to chat {ChatId}", reminder.Id, reminder.User.TelegramId);
                        await reminderService.RequeueReminderAsync(reminder, stoppingToken);
                    }
                }

                if (delivered.Count > 0)
                {
                    var triggeredAt = DateTime.UtcNow;
                    await reminderService.MarkAsSentAsync(delivered.Select(l => l.Reminder), triggeredAt, stoppingToken);
                }

                foreach (var (reminderId, lockValue) in releaseQueue)
                {
                    await reminderService.ReleaseReminderLockAsync(reminderId, lockValue, stoppingToken);
                }

                nextDelay = TimeSpan.FromMilliseconds(200);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error while processing reminders");
            }

            await Task.Delay(nextDelay, stoppingToken);
        }
    }
}
