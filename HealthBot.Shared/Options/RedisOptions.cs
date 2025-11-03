using System;

namespace HealthBot.Shared.Options;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string? ConnectionString { get; set; }
    public string KeyPrefix { get; set; } = "healthbot:";
    public int DefaultTtlMinutes { get; set; } = 30;

    public TimeSpan GetDefaultTtl() => TimeSpan.FromMinutes(DefaultTtlMinutes <= 0 ? 30 : DefaultTtlMinutes);
}
