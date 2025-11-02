using System;

namespace HealthBot.Core.Entities;

public class Reminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TemplateId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ScheduledAt { get; set; }
    public DateTime NextTriggerAt { get; set; }
    public int? RepeatIntervalMinutes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastTriggeredAt { get; set; }
    public User? User { get; set; }
    public ReminderTemplate? Template { get; set; }
}
