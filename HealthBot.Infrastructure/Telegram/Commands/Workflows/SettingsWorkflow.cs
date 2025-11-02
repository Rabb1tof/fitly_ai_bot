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
        await context.SendMessageAsync(
            $"⚙️ Настройки\nТекущая таймзона: {currentTz}",
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
}
