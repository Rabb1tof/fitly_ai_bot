using System;
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

    public readonly record struct ReminderLease(Reminder Reminder, string LockValue);

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
        _dbContext.Entry(reminder).State = EntityState.Detached;
        await EnqueueReminderAsync(reminder, cancellationToken);
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

    public async Task<IReadOnlyList<ReminderLease>> DequeueDueRemindersAsync(DateTime utcNow, DateTime dueHorizonUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_redisOptions.ConnectionString))
        {
            var dueReminders = await _dbContext.Reminders
                .Include(r => r.User)
                .Where(r => r.IsActive && r.NextTriggerAt <= dueHorizonUtc)
                .ToListAsync(cancellationToken);

            return dueReminders
                .Where(r => r.NextTriggerAt <= utcNow)
                .Select(r => new ReminderLease(r, string.Empty))
                .ToList();
        }

        var queueKey = RedisCacheKeys.ReminderQueue();
        var maxScore = new DateTimeOffset(dueHorizonUtc.ToUniversalTime()).ToUnixTimeMilliseconds();
        var batchSize = Math.Max(1, _redisOptions.ReminderBatchSize);

        var candidates = await _cache.RangeByScoreAsync(queueKey, double.NegativeInfinity, maxScore, batchSize, cancellationToken);
        if (candidates.Count == 0)
        {
            return Array.Empty<ReminderLease>();
        }

        var lockTtl = _redisOptions.GetReminderLockTtl();
        var locked = new List<(Guid ReminderId, string LockValue)>(candidates.Count);

        foreach (var (member, _) in candidates)
        {
            if (!Guid.TryParseExact(member, "N", out var reminderId))
            {
                await _cache.RemoveFromSortedSetAsync(queueKey, member, cancellationToken);
                continue;
            }

            var lockValue = Guid.NewGuid().ToString("N");
            var lockKey = RedisCacheKeys.ReminderLock(reminderId);
            if (await _cache.AcquireLockAsync(lockKey, lockValue, lockTtl, cancellationToken))
            {
                locked.Add((reminderId, lockValue));
            }
        }

        if (locked.Count == 0)
        {
            return Array.Empty<ReminderLease>();
        }

        var reminderIds = locked.Select(x => x.ReminderId).ToList();
        var lockMap = locked.ToDictionary(x => x.ReminderId, x => x.LockValue);

        var reminders = await _dbContext.Reminders
            .Include(r => r.User)
            .Where(r => reminderIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        var remindersById = reminders.ToDictionary(r => r.Id);
        var leases = new List<ReminderLease>(reminders.Count);

        foreach (var reminderId in reminderIds)
        {
            if (!remindersById.TryGetValue(reminderId, out var reminder))
            {
                await RemoveReminderFromQueueAsync(reminderId, cancellationToken);
                await ReleaseReminderLockAsync(reminderId, lockMap[reminderId], cancellationToken);
                continue;
            }

            if (!reminder.IsActive)
            {
                await RemoveReminderFromQueueAsync(reminder.Id, cancellationToken);
                await ReleaseReminderLockAsync(reminder.Id, lockMap[reminder.Id], cancellationToken);
                continue;
            }

            if (reminder.NextTriggerAt > utcNow)
            {
                await EnqueueReminderAsync(reminder, cancellationToken);
                await ReleaseReminderLockAsync(reminder.Id, lockMap[reminder.Id], cancellationToken);
                continue;
            }

            await RemoveReminderFromQueueAsync(reminder.Id, cancellationToken);
            leases.Add(new ReminderLease(reminder, lockMap[reminder.Id]));
        }

        return leases;
    }

    public async Task MarkAsSentAsync(IEnumerable<Reminder> reminders, DateTime triggeredAt, CancellationToken cancellationToken = default)
    {
        var affectedUsers = new HashSet<Guid>();
        var requeue = new List<Reminder>();
        var removeFromQueue = new List<Guid>();

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
                removeFromQueue.Add(reminder.Id);
            }

            affectedUsers.Add(reminder.UserId);

            if (reminder.IsActive)
            {
                requeue.Add(reminder);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var reminder in reminders)
        {
            _dbContext.Entry(reminder).State = EntityState.Detached;
        }

        foreach (var reminder in requeue)
        {
            await EnqueueReminderAsync(reminder, cancellationToken);
        }

        if (removeFromQueue.Count > 0)
        {
            await RemoveFromQueueAsync(removeFromQueue, cancellationToken);
        }

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
        _dbContext.Entry(reminder).State = EntityState.Detached;
        await RemoveFromQueueAsync(reminderId, cancellationToken);
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

    public Task<Reminder?> GetReminderWithUserAsync(Guid reminderId, CancellationToken cancellationToken = default)
        => _dbContext.Reminders
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == reminderId, cancellationToken);

    public Task RemoveReminderFromQueueAsync(Guid reminderId, CancellationToken cancellationToken = default)
        => RemoveFromQueueAsync(reminderId, cancellationToken);

    public Task RemoveRemindersFromQueueAsync(IEnumerable<Guid> reminderIds, CancellationToken cancellationToken = default)
        => RemoveFromQueueAsync(reminderIds, cancellationToken);

    public Task ReleaseReminderLockAsync(Guid reminderId, string lockValue, CancellationToken cancellationToken = default)
        => _cache.ReleaseLockAsync(RedisCacheKeys.ReminderLock(reminderId), lockValue, cancellationToken);

    public Task RequeueReminderAsync(Reminder reminder, CancellationToken cancellationToken = default)
        => EnqueueReminderAsync(reminder, cancellationToken);

    private Task EnqueueReminderAsync(Reminder reminder, CancellationToken cancellationToken)
    {
        var score = new DateTimeOffset(reminder.NextTriggerAt.ToUniversalTime()).ToUnixTimeMilliseconds();
        return _cache.AddToSortedSetAsync(RedisCacheKeys.ReminderQueue(), reminder.Id.ToString("N"), score, cancellationToken);
    }

    private Task RemoveFromQueueAsync(Guid reminderId, CancellationToken cancellationToken)
        => _cache.RemoveFromSortedSetAsync(RedisCacheKeys.ReminderQueue(), reminderId.ToString("N"), cancellationToken);

    private Task RemoveFromQueueAsync(IEnumerable<Guid> reminderIds, CancellationToken cancellationToken)
    {
        var members = reminderIds.Select(id => id.ToString("N"));
        return _cache.RemoveFromSortedSetAsync(RedisCacheKeys.ReminderQueue(), members, cancellationToken);
    }

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
