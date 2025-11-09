using System;
using System.Collections.Generic;

namespace HealthBot.Core.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long TelegramId { get; set; }
    public string? Username { get; set; }
    public string? TimeZoneId { get; set; }
    public int? QuietHoursStartMinutes { get; set; }
    public int? QuietHoursEndMinutes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Reminder> Reminders { get; set; } = new List<Reminder>();
}
