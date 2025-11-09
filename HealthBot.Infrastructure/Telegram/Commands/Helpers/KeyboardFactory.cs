using System.Collections.Generic;
using HealthBot.Core.Entities;
using Telegram.Bot.Types.ReplyMarkups;

namespace HealthBot.Infrastructure.Telegram.Commands.Helpers;

public static class KeyboardFactory
{
    public static InlineKeyboardMarkup MainMenu() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("🔔 Напоминания", TelegramCommandNames.CallbackMainReminders) },
        new[] { InlineKeyboardButton.WithCallbackData("🥗 Питание", TelegramCommandNames.CallbackMainNutrition) },
        new[] { InlineKeyboardButton.WithCallbackData("⚙️ Настройки", TelegramCommandNames.CallbackMainSettings) }
    });

    public static InlineKeyboardMarkup ReminderDashboard() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("📋 Активные", TelegramCommandNames.CallbackRemindersList) },
        new[] { InlineKeyboardButton.WithCallbackData("🧰 Кастом", TelegramCommandNames.CallbackCustomNew) },
        new[] { InlineKeyboardButton.WithCallbackData("📚 Готовые шаблоны", TelegramCommandNames.CallbackRemindersTemplates) },
        new[] { InlineKeyboardButton.WithCallbackData("↩️ Назад", TelegramCommandNames.CallbackMenu) }
    });

    public static InlineKeyboardMarkup SettingsMenu() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("🌍 Таймзона", TelegramCommandNames.CallbackSettingsTimezone) },
        new[] { InlineKeyboardButton.WithCallbackData("😴 Тихие часы", TelegramCommandNames.CallbackSettingsQuietHours) },
        new[] { InlineKeyboardButton.WithCallbackData("↩️ В меню", TelegramCommandNames.CallbackMenu) }
    });

    public static InlineKeyboardMarkup BackToMenu() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("↩️ В меню", TelegramCommandNames.CallbackMenu) }
    });

    public static InlineKeyboardMarkup BackToReminders() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("↩️ К напоминаниям", TelegramCommandNames.CallbackMainReminders) }
    });

    public static InlineKeyboardMarkup BackToSettings() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("↩️ К настройкам", TelegramCommandNames.CallbackMainSettings) }
    });

    public static InlineKeyboardMarkup QuietHoursMenu(bool hasQuietHours)
    {
        var rows = new List<List<InlineKeyboardButton>>
        {
            new()
            {
                InlineKeyboardButton.WithCallbackData("✏️ Изменить", TelegramCommandNames.CallbackSettingsQuietHoursEdit)
            }
        };

        if (hasQuietHours)
        {
            rows.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("🛑 Отключить", TelegramCommandNames.CallbackSettingsQuietHoursDisable)
            });
        }

        rows.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("↩️ К настройкам", TelegramCommandNames.CallbackMainSettings)
        });

        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup DelayKeyboard(string prefix, string code) => new(new List<List<InlineKeyboardButton>>
    {
        new()
        {
            InlineKeyboardButton.WithCallbackData("15 мин", $"{prefix}:{code}:15"),
            InlineKeyboardButton.WithCallbackData("30 мин", $"{prefix}:{code}:30")
        },
        new()
        {
            InlineKeyboardButton.WithCallbackData("1 час", $"{prefix}:{code}:60"),
            InlineKeyboardButton.WithCallbackData("3 часа", $"{prefix}:{code}:180")
        },
        new()
        {
            InlineKeyboardButton.WithCallbackData("🔢 Ввести вручную", $"{prefix}:{code}:manual")
        },
        new()
        {
            InlineKeyboardButton.WithCallbackData("↩️ В меню", TelegramCommandNames.CallbackMenu)
        }
    });

    public static InlineKeyboardMarkup RepeatKeyboard(string prefix, string code, int? defaultRepeat)
    {
        var rows = new List<List<InlineKeyboardButton>>();

        if (defaultRepeat.HasValue)
        {
            rows.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("По умолчанию", $"{prefix}:{code}:default")
            });
        }

        rows.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("Без повтора", $"{prefix}:{code}:0"),
            InlineKeyboardButton.WithCallbackData("Каждые 30 мин", $"{prefix}:{code}:30")
        });

        rows.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("Каждый час", $"{prefix}:{code}:60"),
            InlineKeyboardButton.WithCallbackData("Каждые 2 часа", $"{prefix}:{code}:120")
        });

        rows.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("🔢 Ввести вручную", $"{prefix}:{code}:manual")
        });

        rows.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("↩️ К напоминаниям", TelegramCommandNames.CallbackMainReminders)
        });

        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup ReminderList(IReadOnlyList<Reminder> reminders)
    {
        var rows = new List<List<InlineKeyboardButton>>();

        foreach (var reminder in reminders)
        {
            rows.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData(
                    $"❌ {ReminderFormatter.GetReminderDisplayName(reminder)}",
                    $"{TelegramCommandNames.CallbackRemindersDisable}:{reminder.Id:N}")
            });
        }

        rows.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("↩️ К напоминаниям", TelegramCommandNames.CallbackMainReminders)
        });

        return new InlineKeyboardMarkup(rows);
    }
}
