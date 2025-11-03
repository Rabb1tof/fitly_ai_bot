namespace HealthBot.Shared.Options;

public class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public int PollingRestartMinutes { get; set; } = 30;

    public TimeSpan GetPollingRestartInterval()
    {
        if (PollingRestartMinutes <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromMinutes(PollingRestartMinutes);
    }
}
