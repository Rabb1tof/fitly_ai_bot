using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthBot.Core.Entities;
using HealthBot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthBot.Infrastructure.Services;

public class ReminderService
{
    private readonly HealthBotDbContext _dbContext;

    public ReminderService(HealthBotDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Reminder> ScheduleReminderAsync(
        Guid userId,
        string message,
        DateTime scheduledAt,
        int? repeatIntervalMinutes = null,
        Guid? templateId = null,
        CancellationToken cancellationToken = default)
    {
        var reminder = new Reminder
        {
            UserId = userId,
            TemplateId = templateId,
            Message = message,
            ScheduledAt = scheduledAt,
            NextTriggerAt = scheduledAt,
            RepeatIntervalMinutes = repeatIntervalMinutes,
            IsActive = true
        };

        await _dbContext.Reminders.AddAsync(reminder, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return reminder;
    }

    public async Task<IReadOnlyCollection<Reminder>> GetDueRemindersAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Reminders
            .Include(r => r.User)
            .Where(r => r.IsActive && r.NextTriggerAt <= utcNow)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkAsSentAsync(IEnumerable<Reminder> reminders, DateTime triggeredAt, CancellationToken cancellationToken = default)
    {
        foreach (var reminder in reminders)
        {
            reminder.LastTriggeredAt = triggeredAt;

            if (reminder.RepeatIntervalMinutes is { } interval && interval > 0)
            {
                reminder.NextTriggerAt = triggeredAt.AddMinutes(interval);
            }
            else
            {
                reminder.IsActive = false;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<List<ReminderTemplate>> GetReminderTemplatesAsync(CancellationToken cancellationToken = default)
        => _dbContext.ReminderTemplates
            .OrderBy(t => t.Title)
            .ToListAsync(cancellationToken);

    public Task<ReminderTemplate?> GetTemplateByCodeAsync(string code, CancellationToken cancellationToken = default)
        => _dbContext.ReminderTemplates.FirstOrDefaultAsync(t => t.Code == code, cancellationToken);

    public Task<List<Reminder>> GetActiveRemindersForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => _dbContext.Reminders
            .Where(r => r.UserId == userId && r.IsActive)
            .OrderBy(r => r.NextTriggerAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> DeactivateReminderAsync(Guid reminderId, Guid userId, CancellationToken cancellationToken = default)
    {
        var reminder = await _dbContext.Reminders
            .FirstOrDefaultAsync(r => r.Id == reminderId && r.UserId == userId && r.IsActive, cancellationToken);

        if (reminder is null)
        {
            return false;
        }

        reminder.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
