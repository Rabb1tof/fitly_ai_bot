using System;
using System.Collections.Generic;
using System.Linq;
using HealthBot.Core.Entities;
using HealthBot.Infrastructure.Services;

namespace HealthBot.Infrastructure.Telegram.Commands.Helpers;

public static class ReminderFormatter
{
    public static string BuildReminderSummary(Reminder reminder, int index, TimeZoneInfo timeZone)
    {
        var title = reminder.Template?.Title ?? reminder.Message;
        var next = TimeZoneHelper.ConvertUtcToUserTime(reminder.NextTriggerAt, timeZone);
        var repeatPart = reminder.RepeatIntervalMinutes is { } interval and > 0
            ? $"повтор каждые {FormatInterval(interval)}"
            : "без повтора";

        return $"{index}. {title} — {next:dd.MM HH:mm}, {repeatPart}";
    }

    public static string GetReminderDisplayName(Reminder reminder)
    {
        var title = reminder.Template?.Title ?? reminder.Message;
        return title.Length > 32 ? title[..29] + "…" : title;
    }

    public static string FormatInterval(int minutes)
    {
        if (minutes % 1440 == 0)
        {
            var days = minutes / 1440;
            return days == 1 ? "1 день" : $"{days} дн.";
        }

        if (minutes % 60 == 0)
        {
            var hours = minutes / 60;
            return hours == 1 ? "1 час" : $"{hours} ч";
        }

        return $"{minutes} мин";
    }

    public static string BuildReminderListText(IReadOnlyList<Reminder> reminders, TimeZoneInfo timeZone)
    {
        var lines = reminders.Select((reminder, index) => BuildReminderSummary(reminder, index + 1, timeZone));
        return "📋 Активные напоминания:\n" + string.Join("\n", lines);
    }
}
