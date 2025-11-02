using System;

namespace HealthBot.Infrastructure.Telegram;

public sealed class ConversationContext
{
    public ConversationFlow Flow { get; set; } = ConversationFlow.None;
    public ConversationStage Stage { get; set; } = ConversationStage.None;
    public string? TemplateCode { get; set; }
    public Guid? TemplateId { get; set; }
    public string? TemplateTitle { get; set; }
    public int? TemplateDefaultRepeat { get; set; }
    public string? CustomMessage { get; set; }
    public int? FirstDelayMinutes { get; set; }
    public bool ExpectManualInput { get; set; }
    public int? LastBotMessageId { get; set; }

    public void Reset()
    {
        Flow = ConversationFlow.None;
        Stage = ConversationStage.None;
        TemplateCode = null;
        TemplateId = null;
        TemplateTitle = null;
        TemplateDefaultRepeat = null;
        CustomMessage = null;
        FirstDelayMinutes = null;
        ExpectManualInput = false;
    }
}

public enum ConversationFlow
{
    None,
    Template,
    Custom
}

public enum ConversationStage
{
    None,
    AwaitingCustomMessage,
    AwaitingFirstDelayMinutes,
    AwaitingRepeatMinutes,
    AwaitingTimeZoneManual
}
