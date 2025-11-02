using System;
using System.Collections.Generic;

namespace HealthBot.Core.Entities;

public class ReminderTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? DefaultRepeatIntervalMinutes { get; set; }
    public bool IsSystem { get; set; }

    public ICollection<Reminder> Reminders { get; set; } = new List<Reminder>();
}
