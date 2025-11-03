using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthBot.Core.Entities;
using HealthBot.Infrastructure.Data;
using HealthBot.Shared.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HealthBot.Infrastructure.Services;

public class ReminderService
{
    private readonly HealthBotDbContext _dbContext;
    private readonly IRedisCacheService _cache;
    private readonly RedisOptions _redisOptions;

    public ReminderService(
        HealthBotDbContext dbContext,
        IRedisCacheService cache,
        IOptions<RedisOptions> redisOptions)
    {
        _dbContext = dbContext;
        _cache = cache;
        _redisOptions = redisOptions.Value;
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
        await InvalidateUserRemindersCacheAsync(userId, cancellationToken);

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
        var affectedUsers = new HashSet<Guid>();

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

            affectedUsers.Add(reminder.UserId);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var userId in affectedUsers)
        {
            await InvalidateUserRemindersCacheAsync(userId, cancellationToken);
        }
    }

    public Task<List<ReminderTemplate>> GetReminderTemplatesAsync(CancellationToken cancellationToken = default)
        => GetOrCacheReminderTemplatesAsync(cancellationToken);

    public Task<ReminderTemplate?> GetTemplateByCodeAsync(string code, CancellationToken cancellationToken = default)
        => GetTemplateByCodeCachedAsync(code, cancellationToken);

    public Task<List<Reminder>> GetActiveRemindersForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => GetOrCacheUserRemindersAsync(userId, cancellationToken);

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
        await InvalidateUserRemindersCacheAsync(userId, cancellationToken);
        return true;
    }

    private async Task<List<ReminderTemplate>> GetOrCacheReminderTemplatesAsync(CancellationToken cancellationToken)
    {
        var cacheKey = RedisCacheKeys.ReminderTemplates();
        var cached = await _cache.GetAsync<List<ReminderTemplate>>(cacheKey, cancellationToken);
        if (cached is { Count: > 0 })
        {
            return CloneTemplates(cached);
        }

        var templates = await _dbContext.ReminderTemplates
            .AsNoTracking()
            .OrderBy(t => t.Title)
            .ToListAsync(cancellationToken);

        if (templates.Count > 0)
        {
            await _cache.SetAsync(cacheKey, templates, _redisOptions.GetDefaultTtl(), cancellationToken);
        }

        return templates;
    }

    private async Task<ReminderTemplate?> GetTemplateByCodeCachedAsync(string code, CancellationToken cancellationToken)
    {
        var templates = await GetOrCacheReminderTemplatesAsync(cancellationToken);
        return templates.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<Reminder>> GetOrCacheUserRemindersAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cacheKey = RedisCacheKeys.UserReminders(userId);
        var cached = await _cache.GetAsync<List<Reminder>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return CloneReminders(cached);
        }

        var reminders = await _dbContext.Reminders
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.IsActive)
            .OrderBy(r => r.NextTriggerAt)
            .ToListAsync(cancellationToken);

        if (reminders.Count > 0)
        {
            await _cache.SetAsync(cacheKey, reminders, _redisOptions.GetDefaultTtl(), cancellationToken);
        }
        else
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken);
        }

        return reminders;
    }

    private Task InvalidateUserRemindersCacheAsync(Guid userId, CancellationToken cancellationToken)
        => _cache.RemoveAsync(RedisCacheKeys.UserReminders(userId), cancellationToken);

    private static List<ReminderTemplate> CloneTemplates(IEnumerable<ReminderTemplate> templates)
        => templates.Select(CloneTemplate).ToList();

    private static ReminderTemplate CloneTemplate(ReminderTemplate template)
        => new()
        {
            Id = template.Id,
            Code = template.Code,
            Title = template.Title,
            Description = template.Description,
            DefaultRepeatIntervalMinutes = template.DefaultRepeatIntervalMinutes,
            IsSystem = template.IsSystem
        };

    private static List<Reminder> CloneReminders(IEnumerable<Reminder> reminders)
        => reminders.Select(CloneReminder).ToList();

    private static Reminder CloneReminder(Reminder reminder)
        => new()
        {
            Id = reminder.Id,
            UserId = reminder.UserId,
            TemplateId = reminder.TemplateId,
            Message = reminder.Message,
            CreatedAt = reminder.CreatedAt,
            ScheduledAt = reminder.ScheduledAt,
            NextTriggerAt = reminder.NextTriggerAt,
            RepeatIntervalMinutes = reminder.RepeatIntervalMinutes,
            IsActive = reminder.IsActive,
            LastTriggeredAt = reminder.LastTriggeredAt
        };
}
