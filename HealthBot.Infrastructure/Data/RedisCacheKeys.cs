using System;

namespace HealthBot.Infrastructure.Data;

public static class RedisCacheKeys
{
    public static string UserProfile(long telegramId) => $"user:{telegramId}";

    public static string ConversationSession(long chatId) => $"session:{chatId}";

    public static string ReminderTemplates() => "reminder_templates";

    public static string UserReminders(Guid userId) => $"reminders:user:{userId}";

    public static string ReminderQueue() => "reminders:queue";

    public static string ReminderLock(Guid reminderId) => $"lock:reminder:{reminderId}";

    public static string ReminderDueCandidates() => "reminders:due";

    public static string RateLimitMessages(long chatId) => $"rl:msg:{chatId}";

    public static string RateLimitCallbacks(long chatId) => $"rl:cb:{chatId}";
}
