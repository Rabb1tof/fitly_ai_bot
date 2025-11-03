using System;

namespace HealthBot.Shared.Options;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string? ConnectionString { get; set; }
    public string KeyPrefix { get; set; } = "healthbot:";
    public int DefaultTtlMinutes { get; set; } = 30;
    public int MessageRateLimitPerMinute { get; set; } = 30;
    public int CallbackRateLimitPerMinute { get; set; } = 60;
    public int RateLimitWindowSeconds { get; set; } = 60;
    public int ReminderLockSeconds { get; set; } = 30;
    public int ReminderBatchSize { get; set; } = 50;
    public int ReminderLookaheadMinutes { get; set; } = 30;
    public int ReminderWorkerPollSeconds { get; set; } = 5;

    public TimeSpan GetDefaultTtl() => TimeSpan.FromMinutes(DefaultTtlMinutes <= 0 ? 30 : DefaultTtlMinutes);

    public TimeSpan GetRateLimitWindow() => TimeSpan.FromSeconds(RateLimitWindowSeconds <= 0 ? 60 : RateLimitWindowSeconds);

    public TimeSpan GetReminderLockTtl() => TimeSpan.FromSeconds(ReminderLockSeconds <= 0 ? 30 : ReminderLockSeconds);

    public TimeSpan GetReminderWorkerPollInterval()
    {
        var seconds = ReminderWorkerPollSeconds <= 0 ? 5 : ReminderWorkerPollSeconds;
        return TimeSpan.FromSeconds(seconds);
    }
}
