using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using HealthBot.Infrastructure.Services;
using HealthBot.Infrastructure.Telegram.Commands.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types.ReplyMarkups;

namespace HealthBot.Infrastructure.Telegram.Commands.Workflows;

public static class ReminderWorkflow
{
    public static async Task ShowDashboardAsync(CommandContext context)
    {
        var session = context.Session;
        session.Flow = ConversationFlow.Template;
        session.Stage = ConversationStage.None;
        session.ExpectManualInput = false;

        await context.DeleteLastMessageAsync();

        var userTz = context.User.TimeZoneId ?? "UTC";
        var message = $"Выбери действие с напоминаниями.\nТекущая таймзона: {userTz}.";

        await context.SendMessageAsync(message, KeyboardFactory.ReminderDashboard());
    }

    public static async Task ShowNutritionStubAsync(CommandContext context)
    {
        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync(
            "Раздел \"Питание\" в разработке. Следи за обновлениями!",
            KeyboardFactory.BackToMenu());
    }

    public static async Task ShowReminderListAsync(CommandContext context)
    {
        var reminderService = context.Services.GetRequiredService<ReminderService>();
        var reminders = await reminderService.GetActiveRemindersForUserAsync(context.User.Id, context.CancellationToken);

        var timeZoneInfo = TimeZoneHelper.Resolve(context.User.TimeZoneId);

        await context.DeleteLastMessageAsync();

        if (reminders.Count == 0)
        {
            await context.SendMessageAsync("Активных напоминаний нет.", KeyboardFactory.BackToReminders());
            return;
        }

        var text = ReminderFormatter.BuildReminderListText(reminders, timeZoneInfo);
        await context.SendMessageAsync(text, KeyboardFactory.ReminderList(reminders));
    }

    public static async Task ShowReminderTemplatesAsync(CommandContext context)
    {
        var reminderService = context.Services.GetRequiredService<ReminderService>();
        var templates = await reminderService.GetReminderTemplatesAsync(context.CancellationToken);

        var rows = templates
            .Chunk(2)
            .Select(chunk => chunk
                .Select(t => InlineKeyboardButton.WithCallbackData(t.Title, $"{TelegramCommandNames.CallbackTemplateSelect}:{t.Code}"))
                .ToList())
            .ToList();

        rows.Add(new() { InlineKeyboardButton.WithCallbackData("🧰 Кастом", TelegramCommandNames.CallbackCustomNew) });
        rows.Add(new() { InlineKeyboardButton.WithCallbackData("↩️ Назад", TelegramCommandNames.CallbackMainReminders) });

        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync("Выбери шаблон напоминания:", new InlineKeyboardMarkup(rows));
    }

    public static async Task HandleTemplateSelectedAsync(CommandContext context, string[] parts)
    {
        if (parts.Length < 2)
        {
            await ReplyWithError(context, "Не удалось определить шаблон.");
            return;
        }

        var code = parts[1];
        var reminderService = context.Services.GetRequiredService<ReminderService>();
        var template = await reminderService.GetTemplateByCodeAsync(code, context.CancellationToken);
        if (template is null)
        {
            await ReplyWithError(context, "Шаблон не найден.");
            return;
        }

        var session = context.Session;
        session.ResetFlowState();
        session.Flow = ConversationFlow.Template;
        session.Stage = ConversationStage.AwaitingFirstDelayMinutes;
        session.TemplateCode = template.Code;
        session.TemplateId = template.Id;
        session.TemplateTitle = template.Title;
        session.TemplateDefaultRepeat = template.DefaultRepeatIntervalMinutes;

        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync(
            $"Через сколько минут прислать напоминание \"{template.Title}\"?",
            KeyboardFactory.DelayKeyboard(TelegramCommandNames.CallbackTemplateDelay, template.Code));
    }

    public static async Task HandleTemplateDelayAsync(CommandContext context, string[] parts)
    {
        if (parts.Length < 3)
        {
            await ReplyWithError(context, "Не удалось обработать выбранный интервал.");
            return;
        }

        var code = parts[1];
        var session = context.Session;
        var reminderService = context.Services.GetRequiredService<ReminderService>();

        if (!string.Equals(session.TemplateCode, code, StringComparison.Ordinal))
        {
            var template = await reminderService.GetTemplateByCodeAsync(code, context.CancellationToken);
            if (template is null)
            {
                await ReplyWithError(context, "Шаблон не найден.");
                return;
            }

            session.ResetFlowState();
            session.Flow = ConversationFlow.Template;
            session.Stage = ConversationStage.AwaitingFirstDelayMinutes;
            session.TemplateCode = template.Code;
            session.TemplateId = template.Id;
            session.TemplateTitle = template.Title;
            session.TemplateDefaultRepeat = template.DefaultRepeatIntervalMinutes;
        }

        var value = parts[2];
        if (value.Equals("manual", StringComparison.Ordinal))
        {
            session.Stage = ConversationStage.AwaitingFirstDelayMinutes;
            session.ExpectManualInput = true;
            await context.DeleteLastMessageAsync();
            await context.SendMessageAsync("Введи число минут (минимум 1). Если передумал, отправь /cancel.");
            return;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes < 1)
        {
            await ReplyWithError(context, "Нужно указать положительное число минут.");
            return;
        }

        session.FirstDelayMinutes = minutes;
        session.Stage = ConversationStage.AwaitingRepeatMinutes;
        session.ExpectManualInput = false;

        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync(
            "Как часто повторять напоминание? 0 — без повтора.",
            KeyboardFactory.RepeatKeyboard(TelegramCommandNames.CallbackTemplateRepeat, session.TemplateCode!, session.TemplateDefaultRepeat));
    }

    public static async Task HandleTemplateRepeatAsync(CommandContext context, string[] parts)
    {
        var session = context.Session;
        if (session.FirstDelayMinutes is null)
        {
            await ReplyWithError(context, "Сначала выбери время первого напоминания.");
            return;
        }

        if (parts.Length < 3)
        {
            await ReplyWithError(context, "Не удалось обработать повтор.");
            return;
        }

        var value = parts[2];
        if (value.Equals("manual", StringComparison.Ordinal))
        {
            session.Stage = ConversationStage.AwaitingRepeatMinutes;
            session.ExpectManualInput = true;
            await context.DeleteLastMessageAsync();
            await context.SendMessageAsync("Введи число минут для повтора (0 — без повтора). Если передумал, отправь /cancel.");
            return;
        }

        int? repeatMinutes = value switch
        {
            "default" when session.TemplateDefaultRepeat.HasValue => session.TemplateDefaultRepeat,
            _ when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };

        if (repeatMinutes is null || repeatMinutes < 0)
        {
            await ReplyWithError(context, "Некорректное значение повтора.");
            return;
        }

        await FinalizeReminderAsync(context, repeatMinutes);
    }

    public static Task StartCustomFlowAsync(CommandContext context)
    {
        var session = context.Session;
        session.ResetFlowState();
        session.Flow = ConversationFlow.Custom;
        session.Stage = ConversationStage.AwaitingCustomMessage;
        session.ExpectManualInput = true;

        return context.SendMessageAsync("Введи текст напоминания.");
    }

    public static async Task HandleCustomMessageAsync(CommandContext context, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            await context.SendMessageAsync("Напоминание не может быть пустым.");
            return;
        }

        var session = context.Session;
        session.CustomMessage = text;
        session.Stage = ConversationStage.AwaitingFirstDelayMinutes;
        session.ExpectManualInput = false;

        await context.SendMessageAsync(
            "Через сколько минут прислать напоминание?",
            KeyboardFactory.DelayKeyboard(TelegramCommandNames.CallbackCustomDelay, "custom"));
    }

    public static async Task HandleCustomDelayCallbackAsync(CommandContext context, string[] parts)
    {
        var session = context.Session;
        if (session.Flow != ConversationFlow.Custom || session.CustomMessage is null)
        {
            await ReplyWithError(context, "Сначала введи текст напоминания.");
            return;
        }

        if (parts.Length < 3)
        {
            await ReplyWithError(context, "Не удалось обработать выбранный интервал.");
            return;
        }

        var value = parts[2];
        if (value.Equals("manual", StringComparison.Ordinal))
        {
            session.Stage = ConversationStage.AwaitingFirstDelayMinutes;
            session.ExpectManualInput = true;
            await context.DeleteLastMessageAsync();
            await context.SendMessageAsync("Введи число минут до первого напоминания (минимум 1).");
            return;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes < 1)
        {
            await ReplyWithError(context, "Нужно указать положительное число минут.");
            return;
        }

        session.FirstDelayMinutes = minutes;
        session.Stage = ConversationStage.AwaitingRepeatMinutes;
        session.ExpectManualInput = false;

        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync(
            "Как часто повторять? 0 — без повтора.",
            KeyboardFactory.RepeatKeyboard(TelegramCommandNames.CallbackCustomRepeat, "custom", null));
    }

    public static async Task HandleCustomRepeatCallbackAsync(CommandContext context, string[] parts)
    {
        var session = context.Session;
        if (session.FirstDelayMinutes is null)
        {
            await ReplyWithError(context, "Сначала укажи время первого напоминания.");
            return;
        }

        if (parts.Length < 3)
        {
            await ReplyWithError(context, "Не удалось обработать повтор.");
            return;
        }

        var value = parts[2];
        if (value.Equals("manual", StringComparison.Ordinal))
        {
            session.Stage = ConversationStage.AwaitingRepeatMinutes;
            session.ExpectManualInput = true;
            await context.DeleteLastMessageAsync();
            await context.SendMessageAsync("Введи число минут для повтора (0 — без повтора).");
            return;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var repeat) || repeat < 0)
        {
            await ReplyWithError(context, "Нужно указать неотрицательное число.");
            return;
        }

        await FinalizeReminderAsync(context, repeat);
    }

    public static async Task HandleManualDelayAsync(CommandContext context, string text)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes < 1)
        {
            await ReplyWithError(context, "Нужно указать положительное число минут.");
            return;
        }

        var session = context.Session;
        session.FirstDelayMinutes = minutes;
        session.ExpectManualInput = false;
        session.Stage = ConversationStage.AwaitingRepeatMinutes;

        var repeatPrefix = session.Flow == ConversationFlow.Template
            ? TelegramCommandNames.CallbackTemplateRepeat
            : TelegramCommandNames.CallbackCustomRepeat;
        var code = session.Flow == ConversationFlow.Template ? session.TemplateCode ?? "custom" : "custom";
        var defaultRepeat = session.Flow == ConversationFlow.Template ? session.TemplateDefaultRepeat : null;

        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync(
            "Как часто повторять напоминание? 0 — без повтора.",
            KeyboardFactory.RepeatKeyboard(repeatPrefix, code, defaultRepeat));
    }

    public static async Task HandleManualRepeatAsync(CommandContext context, string text)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var repeat) || repeat < 0)
        {
            await ReplyWithError(context, "Нужно указать неотрицательное число.");
            return;
        }

        await FinalizeReminderAsync(context, repeat);
    }

    public static async Task HandleDisableReminderAsync(CommandContext context, string[] parts)
    {
        if (parts.Length < 2)
        {
            await ReplyWithError(context, "Не удалось определить напоминание.");
            return;
        }

        if (!Guid.TryParseExact(parts[1], "N", out var reminderId))
        {
            await ReplyWithError(context, "Некорректный идентификатор напоминания.");
            return;
        }

        var reminderService = context.Services.GetRequiredService<ReminderService>();
        var success = await reminderService.DeactivateReminderAsync(reminderId, context.User.Id, context.CancellationToken);

        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync(
            success ? "Напоминание отключено." : "Напоминание уже отключено или не найдено.");

        await ShowReminderListAsync(context);
    }

    private static async Task FinalizeReminderAsync(CommandContext context, int? repeatMinutes)
    {
        var session = context.Session;
        if (session.FirstDelayMinutes is null)
        {
            await ReplyWithError(context, "Сначала выбери время первого напоминания.");
            return;
        }

        var messageText = session.Flow switch
        {
            ConversationFlow.Template => session.TemplateTitle ?? "Напоминание",
            ConversationFlow.Custom => session.CustomMessage ?? "Напоминание",
            _ => session.CustomMessage ?? session.TemplateTitle ?? "Напоминание"
        };

        var scheduledAt = DateTime.UtcNow.AddMinutes(session.FirstDelayMinutes.Value);
        var repeatValue = repeatMinutes is > 0 ? repeatMinutes : null;
        var templateId = session.Flow == ConversationFlow.Template ? session.TemplateId : null;

        var reminderService = context.Services.GetRequiredService<ReminderService>();
        var reminder = await reminderService.ScheduleReminderAsync(
            context.User.Id,
            messageText,
            scheduledAt,
            repeatValue,
            templateId,
            context.CancellationToken);

        session.ResetFlowState();

        var userTimeZone = TimeZoneHelper.Resolve(context.User.TimeZoneId);
        var nextTriggerLocal = TimeZoneHelper.ConvertUtcToUserTime(reminder.NextTriggerAt, userTimeZone);
        var repeatText = repeatValue.HasValue
            ? $" Повтор каждые {ReminderFormatter.FormatInterval(repeatValue.Value).ToLowerInvariant()}."
            : " Без повтора.";

        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync(
            $"Готово! Напоминание \"{messageText}\" запланировано на {nextTriggerLocal:dd.MM HH:mm} ({userTimeZone.Id})." + repeatText,
            KeyboardFactory.BackToMenu());
    }

    private static async Task ReplyWithError(CommandContext context, string message)
    {
        await context.DeleteLastMessageAsync();
        await context.SendMessageAsync(message);
    }
}
