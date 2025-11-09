using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HealthBot.Infrastructure.Services;
using HealthBot.Infrastructure.Telegram.Commands.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types.ReplyMarkups;

namespace HealthBot.Infrastructure.Telegram.Commands.Workflows;

public static class SettingsWorkflow
{
    private static readonly string[] PopularTimeZoneIds =
    {
        "Europe/Moscow",
        "Europe/Kyiv",
        "Europe/Minsk",
        "Asia/Almaty",
        "Asia/Yekaterinburg",
        "Asia/Vladivostok"
    };

    public static async Task ShowSettingsMenuAsync(CommandContext context)
    {
        var session = context.Session;
        session.Stage = ConversationStage.None;
        session.ExpectManualInput = false;

        await context.DeleteLastMessageAsync();

        var currentTz = context.User.TimeZoneId ?? "не задана";
        var quietHoursText = FormatQuietHours(context.User);
        await context.SendMessageAsync(
            $"⚙️ Настройки\nТекущая таймзона: {currentTz}\nТихие часы: {quietHoursText}",
            KeyboardFactory.SettingsMenu());
    }

    public static async Task ShowTimezoneMenuAsync(CommandContext context)
    {
        var session = context.Session;
        session.Stage = ConversationStage.None;
        session.ExpectManualInput = false;

        await context.DeleteLastMessageAsync();

        var rows = PopularTimeZoneIds
            .Select(tz => new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData(tz, $"{TelegramCommandNames.CallbackSettingsTimezoneSelect}:{tz}")
            })
            .ToList();

        rows.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("🔢 Ввести вручную", TelegramCommandNames.CallbackSettingsTimezoneManual)
        });

        rows.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("↩️ Назад", TelegramCommandNames.CallbackMainSettings)
        });

        await context.SendMessageAsync(
            "Выбери таймзону или введи вручную (например, Europe/Moscow)",
            new InlineKeyboardMarkup(rows));
    }

    public static async Task ShowQuietHoursMenuAsync(CommandContext context)
    {
        var session = context.Session;
        session.Stage = ConversationStage.None;
        session.ExpectManualInput = false;

        await context.DeleteLastMessageAsync();

        var hasQuietHours = context.User.QuietHoursStartMinutes.HasValue && context.User.QuietHoursEndMinutes.HasValue;
        var quietHoursText = FormatQuietHours(context.User);
        var tzText = context.User.TimeZoneId ?? "не задана";
        var warning = context.User.TimeZoneId is null
            ? "\n⚠️ Рекомендуем сначала указать таймзону, иначе тихие часы могут работать некорректно."
            : string.Empty;

        var message = hasQuietHours
            ? $"😴 Тихие часы\nТекущие тихие часы: {quietHoursText}.\nВсе времена указываются по твоей таймзоне ({tzText})."
            : $"😴 Тихие часы пока не заданы.\nВсе времена указываются по твоей таймзоне ({tzText}).";

        await context.SendMessageAsync(message + warning, KeyboardFactory.QuietHoursMenu(hasQuietHours));
    }

    public static async Task StartQuietHoursEditAsync(CommandContext context)
    {
        var session = context.Session;
        session.Stage = ConversationStage.AwaitingQuietHoursStart;
        session.ExpectManualInput = true;
        session.PendingQuietHoursStartMinutes = null;
        session.PendingQuietHoursEndMinutes = null;

        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync("Введи начало тихих часов в формате ЧЧ:ММ (например, 23:00).");
    }

    public static async Task HandleQuietHoursStartAsync(CommandContext context, string text)
    {
        if (!TryParseTimeToMinutes(text, out var minutes))
        {
            await context.DeleteLastMessageAsync();
            await context.SendMessageAsync("Не удалось распознать время. Укажи его в формате ЧЧ:ММ, например, 23:00.");
            return;
        }

        var session = context.Session;
        session.PendingQuietHoursStartMinutes = minutes;
        session.Stage = ConversationStage.AwaitingQuietHoursEnd;
        session.ExpectManualInput = true;

        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync("Теперь введи конец тихих часов в формате ЧЧ:ММ (например, 07:00).");
    }

    public static async Task HandleQuietHoursEndAsync(CommandContext context, string text)
    {
        var session = context.Session;
        if (session.PendingQuietHoursStartMinutes is null)
        {
            session.Stage = ConversationStage.None;
            session.ExpectManualInput = false;
            await context.DeleteLastMessageAsync();
            await context.SendMessageAsync(
                "Что-то пошло не так. Попробуй настроить тихие часы заново.",
                KeyboardFactory.BackToSettings());
            return;
        }

        if (!TryParseTimeToMinutes(text, out var endMinutes))
        {
            await context.DeleteLastMessageAsync();
            await context.SendMessageAsync("Не удалось распознать время. Укажи его в формате ЧЧ:ММ, например, 07:00.");
            return;
        }

        var startMinutes = session.PendingQuietHoursStartMinutes.Value;
        if (startMinutes == endMinutes)
        {
            await context.DeleteLastMessageAsync();
            await context.SendMessageAsync("Начало и конец тихих часов не могут совпадать. Укажи другое время.");
            return;
        }

        await ApplyQuietHoursAsync(context, startMinutes, endMinutes);
    }

    public static async Task DisableQuietHoursAsync(CommandContext context)
    {
        var session = context.Session;
        session.Stage = ConversationStage.None;
        session.ExpectManualInput = false;
        session.PendingQuietHoursStartMinutes = null;
        session.PendingQuietHoursEndMinutes = null;

        var userService = context.Services.GetRequiredService<UserService>();
        await userService.SetQuietHoursAsync(context.User, null, null, context.CancellationToken);

        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync("Тихие часы отключены.", KeyboardFactory.BackToSettings());
    }

    public static async Task HandleTimezoneSelectAsync(CommandContext context, string[] parts)
    {
        if (parts.Length < 2)
        {
            await ReplyWithError(context, "Не удалось определить таймзону.");
            return;
        }

        var tzCandidate = parts[1];
        if (!TimeZoneHelper.TryResolve(tzCandidate, out var timeZoneInfo))
        {
            await ReplyWithError(context, "Не удалось распознать таймзону. Попробуй снова.");
            return;
        }

        var userService = context.Services.GetRequiredService<UserService>();
        await userService.SetUserTimeZoneAsync(context.User, timeZoneInfo.Id, context.CancellationToken);
        context.User.TimeZoneId = timeZoneInfo.Id;

        context.Session.Stage = ConversationStage.None;
        context.Session.ExpectManualInput = false;

        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync(
            $"Таймзона обновлена на {timeZoneInfo.Id}.",
            KeyboardFactory.BackToSettings());
    }

    public static Task StartManualTimezoneInputAsync(CommandContext context)
    {
        var session = context.Session;
        session.Stage = ConversationStage.AwaitingTimeZoneManual;
        session.ExpectManualInput = true;

        return context.SendMessageAsync("Введи идентификатор таймзоны (например, Europe/Moscow).");
    }

    public static async Task HandleManualTimezoneAsync(CommandContext context, string text)
    {
        var candidate = text.Trim();
        if (!TimeZoneHelper.TryResolve(candidate, out var timeZoneInfo))
        {
            await context.DeleteLastMessageAsync();
            await context.SendMessageAsync(
                "Не удалось распознать таймзону. Попробуй снова или выбери из списка.",
                KeyboardFactory.BackToSettings());
            return;
        }

        var userService = context.Services.GetRequiredService<UserService>();
        await userService.SetUserTimeZoneAsync(context.User, timeZoneInfo.Id, context.CancellationToken);
        context.User.TimeZoneId = timeZoneInfo.Id;

        var session = context.Session;
        session.Stage = ConversationStage.None;
        session.ExpectManualInput = false;

        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync(
            $"Таймзона обновлена на {timeZoneInfo.Id}.",
            KeyboardFactory.BackToSettings());
    }

    private static async Task ReplyWithError(CommandContext context, string message)
    {
        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync(message);
    }

    private static async Task ApplyQuietHoursAsync(CommandContext context, int startMinutes, int endMinutes)
    {
        var userService = context.Services.GetRequiredService<UserService>();
        await userService.SetQuietHoursAsync(context.User, startMinutes, endMinutes, context.CancellationToken);

        var session = context.Session;
        session.Stage = ConversationStage.None;
        session.ExpectManualInput = false;
        session.PendingQuietHoursStartMinutes = null;
        session.PendingQuietHoursEndMinutes = null;

        await context.DeleteLastMessageAsync();
        var tz = context.User.TimeZoneId ?? "не задана";
        await context.SendMessageAsync(
            $"Тихие часы установлены: {FormatTime(startMinutes)} — {FormatTime(endMinutes)} (таймзона: {tz}).",
            KeyboardFactory.BackToSettings());
    }

    private static bool TryParseTimeToMinutes(string text, out int minutes)
    {
        minutes = default;

        var parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var hours) || !int.TryParse(parts[1], out var mins))
        {
            return false;
        }

        if (hours is < 0 or > 23 || mins is < 0 or > 59)
        {
            return false;
        }

        minutes = hours * 60 + mins;
        return true;
    }

    private static string FormatQuietHours(Core.Entities.User user)
    {
        if (user.QuietHoursStartMinutes is { } start && user.QuietHoursEndMinutes is { } end)
        {
            return $"{FormatTime(start)} — {FormatTime(end)}";
        }

        return "не заданы";
    }

    private static string FormatTime(int minutes)
        => TimeSpan.FromMinutes(minutes).ToString("hh\\:mm");
}
