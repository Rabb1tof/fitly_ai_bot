using System;

namespace HealthBot.Core.Entities;

public class ConversationSession
{
    public long ChatId { get; set; }
    public int Flow { get; set; }
    public int Stage { get; set; }
    public string? TemplateCode { get; set; }
    public Guid? TemplateId { get; set; }
    public string? TemplateTitle { get; set; }
    public int? TemplateDefaultRepeat { get; set; }
    public string? CustomMessage { get; set; }
    public int? FirstDelayMinutes { get; set; }
    public int? PendingQuietHoursStartMinutes { get; set; }
    public int? PendingQuietHoursEndMinutes { get; set; }
    public bool ExpectManualInput { get; set; }
    public int? LastBotMessageId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
