using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthBot.Core.Entities;
using CoreUser = HealthBot.Core.Entities.User;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace HealthBot.Infrastructure.Services;

public class TelegramUpdateHandler : IUpdateHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramUpdateHandler> _logger;
    private readonly ConcurrentDictionary<long, ConversationContext> _sessions = new();

    private const string CallbackMenu = "menu";
    private const string CallbackTemplateSelect = "tpl";
    private const string CallbackTemplateDelay = "tpl_delay";
    private const string CallbackTemplateRepeat = "tpl_repeat";
    private const string CallbackCustomNew = "custom_new";
    private const string CallbackCustomDelay = "custom_delay";
    private const string CallbackCustomRepeat = "custom_repeat";
    private const string CallbackRemindersList = "reminders_list";
    private const string CallbackRemindersDisable = "reminders_disable";

    public TelegramUpdateHandler(IServiceScopeFactory scopeFactory, ILogger<TelegramUpdateHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            switch (update.Type)
            {
                case UpdateType.Message when update.Message is not null:
                    await HandleMessageAsync(botClient, update.Message, cancellationToken);
                    break;
                case UpdateType.CallbackQuery when update.CallbackQuery is not null:
                    await HandleCallbackAsync(botClient, update.CallbackQuery, cancellationToken);
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Ошибка при обработке обновления Telegram");
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiException => $"Telegram API Error [{apiException.ErrorCode}]: {apiException.Message}",
            _ => exception.Message
        };

        _logger.LogError(exception, "Ошибка при обработке обновлений ({Source}): {Message}", source, errorMessage);
        return Task.CompletedTask;
    }

    private async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        if (message.Chat.Type is not ChatType.Private)
        {
            _logger.LogDebug("Ignoring non-private chat {ChatId}", message.Chat.Id);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
        var reminderService = scope.ServiceProvider.GetRequiredService<ReminderService>();

        var username = message.From?.Username ?? message.Chat.Username;
        var user = await userService.RegisterUserAsync(message.Chat.Id, username, cancellationToken);
        var session = GetSession(message.Chat.Id);

        if (string.IsNullOrWhiteSpace(message.Text))
        {
            await DeleteLastBotMessageAsync(botClient, message.Chat.Id, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, message.Chat.Id, session,
                "😅 Пока понимаю только текстовые команды.",
                cancellationToken: cancellationToken);
            return;
        }

        var text = message.Text.Trim();

        if (text.Equals("/start", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("/menu", StringComparison.OrdinalIgnoreCase))
        {
            session.Reset();
            await DeleteLastBotMessageAsync(botClient, message.Chat.Id, session, cancellationToken);
            var introMessage = "Привет! Я HealthBot 🩺\n\nВыбери готовое напоминание или создай своё.";
            await SendMainMenuAsync(botClient, reminderService, message.Chat.Id, session, cancellationToken, introMessage);
            return;
        }

        if (text.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
        {
            session.Reset();
            await DeleteLastBotMessageAsync(botClient, message.Chat.Id, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, message.Chat.Id, session,
                "Диалог сброшен. Используй /menu, чтобы начать заново.",
                cancellationToken: cancellationToken);
            return;
        }

        switch (session.Stage)
        {
            case ConversationStage.AwaitingCustomMessage:
                await HandleCustomMessageAsync(botClient, reminderService, user, message.Chat.Id, text, session, cancellationToken);
                return;
            case ConversationStage.AwaitingFirstDelayMinutes when session.ExpectManualInput:
                await HandleManualDelayAsync(botClient, reminderService, user, message.Chat.Id, text, session, cancellationToken);
                return;
            case ConversationStage.AwaitingRepeatMinutes when session.ExpectManualInput:
                await HandleManualRepeatAsync(botClient, reminderService, user, message.Chat.Id, text, session, cancellationToken);
                return;
        }

        await DeleteLastBotMessageAsync(botClient, message.Chat.Id, session, cancellationToken);
        await SendTrackedMessageAsync(botClient, message.Chat.Id, session,
            "Я пока не понимаю это сообщение. Используй /menu для управления напоминаниями.",
            cancellationToken: cancellationToken);
    }

    private async Task HandleCallbackAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message?.Chat.Id ?? callbackQuery.From.Id;

        using var scope = _scopeFactory.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
        var reminderService = scope.ServiceProvider.GetRequiredService<ReminderService>();

        var username = callbackQuery.From.Username;
        var user = await userService.RegisterUserAsync(chatId, username, cancellationToken);
        var session = GetSession(chatId);

        var data = callbackQuery.Data ?? string.Empty;
        var parts = data.Split(':', StringSplitOptions.RemoveEmptyEntries);

        try
        {
            switch (parts.FirstOrDefault())
            {
                case CallbackMenu:
                    session.Reset();
                    await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
                    await SendMainMenuAsync(botClient, reminderService, chatId, session, cancellationToken);
                    return;

                case CallbackTemplateSelect:
                    await HandleTemplateSelectedAsync(botClient, reminderService, chatId, parts, session, cancellationToken);
                    break;

                case CallbackTemplateDelay:
                    await HandleTemplateDelayCallbackAsync(botClient, reminderService, user, chatId, parts, session, cancellationToken);
                    break;

                case CallbackTemplateRepeat:
                    await HandleTemplateRepeatCallbackAsync(botClient, reminderService, user, chatId, parts, session, cancellationToken);
                    break;

                case CallbackCustomNew:
                    await HandleCustomStartAsync(botClient, chatId, session, cancellationToken);
                    break;

                case CallbackCustomDelay:
                    await HandleCustomDelayCallbackAsync(botClient, reminderService, user, chatId, parts, session, cancellationToken);
                    break;

                case CallbackCustomRepeat:
                    await HandleCustomRepeatCallbackAsync(botClient, reminderService, user, chatId, parts, session, cancellationToken);
                    break;

                case CallbackRemindersList:
                    session.Reset();
                    await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
                    await ShowReminderListAsync(botClient, reminderService, user, chatId, session, cancellationToken);
                    return;

                case CallbackRemindersDisable:
                    await HandleDisableReminderCallbackAsync(botClient, reminderService, user, chatId, parts, session, cancellationToken);
                    break;

                default:
                    await botClient.AnswerCallbackQuery(callbackQuery.Id, text: "Неизвестное действие", cancellationToken: cancellationToken);
                    return;
            }

            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Ошибка при обработке callbackData {Data}", data);
            await botClient.AnswerCallbackQuery(callbackQuery.Id, text: "Произошла ошибка", cancellationToken: cancellationToken);
        }
    }

    private ConversationContext GetSession(long chatId)
        => _sessions.GetOrAdd(chatId, _ => new ConversationContext());

    private async Task SendMainMenuAsync(ITelegramBotClient botClient, ReminderService reminderService, long chatId, ConversationContext session, CancellationToken cancellationToken, string? messageText = null)
    {
        var templates = await reminderService.GetReminderTemplatesAsync(cancellationToken);

        var templateButtons = templates
            .Select(t => InlineKeyboardButton.WithCallbackData(t.Title, $"{CallbackTemplateSelect}:{t.Code}"))
            .ToList();

        var rows = new List<List<InlineKeyboardButton>>();
        foreach (var chunk in templateButtons.Chunk(2))
        {
            rows.Add(chunk.ToList());
        }

        rows.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("➕ Кастомное напоминание", CallbackCustomNew),
            InlineKeyboardButton.WithCallbackData("📋 Мои напоминания", CallbackRemindersList)
        });

        var markup = new InlineKeyboardMarkup(rows);

        await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
        var text = messageText ?? "Выбери один из шаблонов или создай своё напоминание:";
        await SendTrackedMessageAsync(botClient, chatId, session,
            text,
            replyMarkup: markup,
            cancellationToken: cancellationToken);
    }

    private async Task ShowReminderListAsync(ITelegramBotClient botClient, ReminderService reminderService, CoreUser user, long chatId, ConversationContext session, CancellationToken cancellationToken)
    {
        var reminders = await reminderService.GetActiveRemindersForUserAsync(user.Id, cancellationToken);

        if (reminders.Count == 0)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session,
                "Активных напоминаний нет. Нажми кнопку ниже, чтобы вернуться в меню.",
                replyMarkup: BuildBackToMenuKeyboard(),
                cancellationToken: cancellationToken);
            return;
        }

        var lines = reminders.Select((reminder, index) => BuildReminderSummary(reminder, index + 1));
        var text = "📋 Активные напоминания:\n" + string.Join("\n", lines);

        await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
        await SendTrackedMessageAsync(botClient, chatId, session,
            text,
            replyMarkup: BuildReminderListKeyboard(reminders),
            cancellationToken: cancellationToken);
    }

    private async Task HandleTemplateSelectedAsync(ITelegramBotClient botClient, ReminderService reminderService, long chatId, string[] parts, ConversationContext session, CancellationToken cancellationToken)
    {
        if (parts.Length < 2)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Не удалось определить шаблон.", cancellationToken: cancellationToken);
            return;
        }

        var code = parts[1];
        var template = await reminderService.GetTemplateByCodeAsync(code, cancellationToken);
        if (template is null)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Шаблон не найден.", cancellationToken: cancellationToken);
            return;
        }

        session.Reset();
        session.Flow = ConversationFlow.Template;
        session.Stage = ConversationStage.AwaitingFirstDelayMinutes;
        session.TemplateCode = template.Code;
        session.TemplateId = template.Id;
        session.TemplateTitle = template.Title;
        session.TemplateDefaultRepeat = template.DefaultRepeatIntervalMinutes;

        await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
        await SendTrackedMessageAsync(botClient, chatId, session,
            $"Через сколько минут прислать напоминание \"{template.Title}\"?",
            replyMarkup: BuildDelayKeyboard(CallbackTemplateDelay, template.Code),
            cancellationToken: cancellationToken);
    }

    private async Task HandleTemplateDelayCallbackAsync(ITelegramBotClient botClient, ReminderService reminderService, CoreUser user, long chatId, string[] parts, ConversationContext session, CancellationToken cancellationToken)
    {
        if (parts.Length < 3)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Не удалось обработать выбранный интервал.", cancellationToken: cancellationToken);
            return;
        }

        var code = parts[1];
        if (!string.Equals(session.TemplateCode, code, StringComparison.Ordinal))
        {
            var template = await reminderService.GetTemplateByCodeAsync(code, cancellationToken);
            if (template is null)
            {
                await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
                await SendTrackedMessageAsync(botClient, chatId, session, "Шаблон не найден.", cancellationToken: cancellationToken);
                return;
            }

            session.Reset();
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
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session,
                "Введи число минут (минимум 1). Если передумал, отправь /cancel.",
                cancellationToken: cancellationToken);
            return;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes < 1)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Нужно указать положительное число минут.", cancellationToken: cancellationToken);
            return;
        }

        session.FirstDelayMinutes = minutes;
        session.Stage = ConversationStage.AwaitingRepeatMinutes;
        session.ExpectManualInput = false;

        await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
        await SendTrackedMessageAsync(botClient, chatId, session,
            "Как часто повторять напоминание? 0 — без повтора.",
            replyMarkup: BuildRepeatKeyboard(CallbackTemplateRepeat, session.TemplateCode!, session.TemplateDefaultRepeat),
            cancellationToken: cancellationToken);
    }

    private async Task HandleTemplateRepeatCallbackAsync(ITelegramBotClient botClient, ReminderService reminderService, CoreUser user, long chatId, string[] parts, ConversationContext session, CancellationToken cancellationToken)
    {
        if (session.FirstDelayMinutes is null)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Сначала выбери время первого напоминания.", cancellationToken: cancellationToken);
            return;
        }

        if (parts.Length < 3)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Не удалось обработать повтор.", cancellationToken: cancellationToken);
            return;
        }

        var value = parts[2];
        if (value.Equals("manual", StringComparison.Ordinal))
        {
            session.Stage = ConversationStage.AwaitingRepeatMinutes;
            session.ExpectManualInput = true;
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session,
                "Введи число минут для повтора (0 — без повтора). Если передумал, отправь /cancel.",
                cancellationToken: cancellationToken);
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
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Некорректное значение повтора.", cancellationToken: cancellationToken);
            return;
        }

        await FinalizeReminderAsync(botClient, reminderService, user, chatId, session, repeatMinutes, cancellationToken);
    }

    private Task HandleCustomStartAsync(ITelegramBotClient botClient, long chatId, ConversationContext session, CancellationToken cancellationToken)
    {
        session.Reset();
        session.Flow = ConversationFlow.Custom;
        session.Stage = ConversationStage.AwaitingCustomMessage;
        session.ExpectManualInput = true;

        return SendTrackedMessageAsync(botClient, chatId, session,
            "Введи текст напоминания.",
            cancellationToken: cancellationToken);
    }

    private Task HandleCustomMessageAsync(ITelegramBotClient botClient, ReminderService reminderService, CoreUser user, long chatId, string text, ConversationContext session, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return SendTrackedMessageAsync(botClient, chatId, session, "Напоминание не может быть пустым.", cancellationToken: cancellationToken);
        }

        session.CustomMessage = text;
        session.Stage = ConversationStage.AwaitingFirstDelayMinutes;
        session.ExpectManualInput = false;

        return SendTrackedMessageAsync(botClient, chatId, session,
            "Через сколько минут прислать напоминание?",
            replyMarkup: BuildDelayKeyboard(CallbackCustomDelay, "custom"),
            cancellationToken: cancellationToken);
    }

    private async Task HandleCustomDelayCallbackAsync(ITelegramBotClient botClient, ReminderService reminderService, CoreUser user, long chatId, string[] parts, ConversationContext session, CancellationToken cancellationToken)
    {
        if (session.Flow != ConversationFlow.Custom || session.CustomMessage is null)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Сначала введи текст напоминания.", cancellationToken: cancellationToken);
            return;
        }

        if (parts.Length < 3)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Не удалось обработать выбранный интервал.", cancellationToken: cancellationToken);
            return;
        }

        var value = parts[2];
        if (value.Equals("manual", StringComparison.Ordinal))
        {
            session.Stage = ConversationStage.AwaitingFirstDelayMinutes;
            session.ExpectManualInput = true;
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session,
                "Введи число минут до первого напоминания (минимум 1).",
                cancellationToken: cancellationToken);
            return;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes < 1)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Нужно указать положительное число минут.", cancellationToken: cancellationToken);
            return;
        }

        session.FirstDelayMinutes = minutes;
        session.Stage = ConversationStage.AwaitingRepeatMinutes;
        session.ExpectManualInput = false;

        await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
        await SendTrackedMessageAsync(botClient, chatId, session,
            "Как часто повторять? 0 — без повтора.",
            replyMarkup: BuildRepeatKeyboard(CallbackCustomRepeat, "custom", null),
            cancellationToken: cancellationToken);
    }

    private async Task HandleCustomRepeatCallbackAsync(ITelegramBotClient botClient, ReminderService reminderService, CoreUser user, long chatId, string[] parts, ConversationContext session, CancellationToken cancellationToken)
    {
        if (session.FirstDelayMinutes is null)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Сначала укажи время первого напоминания.", cancellationToken: cancellationToken);
            return;
        }

        if (parts.Length < 3)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Не удалось обработать повтор.", cancellationToken: cancellationToken);
            return;
        }

        var value = parts[2];
        if (value.Equals("manual", StringComparison.Ordinal))
        {
            session.Stage = ConversationStage.AwaitingRepeatMinutes;
            session.ExpectManualInput = true;
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session,
                "Введи число минут для повтора (0 — без повтора).",
                cancellationToken: cancellationToken);
            return;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var repeat) || repeat < 0)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Нужно указать неотрицательное число.", cancellationToken: cancellationToken);
            return;
        }

        await FinalizeReminderAsync(botClient, reminderService, user, chatId, session, repeat, cancellationToken);
    }

    private async Task HandleManualDelayAsync(ITelegramBotClient botClient, ReminderService reminderService, CoreUser user, long chatId, string text, ConversationContext session, CancellationToken cancellationToken)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes < 1)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Нужно указать положительное число минут.", cancellationToken: cancellationToken);
            return;
        }

        session.FirstDelayMinutes = minutes;
        session.ExpectManualInput = false;
        session.Stage = ConversationStage.AwaitingRepeatMinutes;

        var repeatPrefix = session.Flow == ConversationFlow.Template ? CallbackTemplateRepeat : CallbackCustomRepeat;
        var code = session.Flow == ConversationFlow.Template ? session.TemplateCode ?? "custom" : "custom";
        var defaultRepeat = session.Flow == ConversationFlow.Template ? session.TemplateDefaultRepeat : null;

        await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
        await SendTrackedMessageAsync(botClient, chatId, session,
            "Как часто повторять напоминание? 0 — без повтора.",
            replyMarkup: BuildRepeatKeyboard(repeatPrefix, code, defaultRepeat),
            cancellationToken: cancellationToken);
    }

    private async Task HandleManualRepeatAsync(ITelegramBotClient botClient, ReminderService reminderService, CoreUser user, long chatId, string text, ConversationContext session, CancellationToken cancellationToken)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var repeat) || repeat < 0)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Нужно указать неотрицательное число.", cancellationToken: cancellationToken);
            return;
        }

        await FinalizeReminderAsync(botClient, reminderService, user, chatId, session, repeat, cancellationToken);
    }

    private async Task HandleDisableReminderCallbackAsync(ITelegramBotClient botClient, ReminderService reminderService, CoreUser user, long chatId, string[] parts, ConversationContext session, CancellationToken cancellationToken)
    {
        if (parts.Length < 2)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Не удалось определить напоминание.", cancellationToken: cancellationToken);
            return;
        }

        if (!Guid.TryParseExact(parts[1], "N", out var reminderId))
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Некорректный идентификатор напоминания.", cancellationToken: cancellationToken);
            return;
        }

        var success = await reminderService.DeactivateReminderAsync(reminderId, user.Id, cancellationToken);
        if (success)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Напоминание отключено.", cancellationToken: cancellationToken);
        }
        else
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Напоминание уже отключено или не найдено.", cancellationToken: cancellationToken);
        }

        await ShowReminderListAsync(botClient, reminderService, user, chatId, session, cancellationToken);
    }

    private async Task FinalizeReminderAsync(ITelegramBotClient botClient, ReminderService reminderService, CoreUser user, long chatId, ConversationContext session, int? repeatMinutes, CancellationToken cancellationToken)
    {
        if (session.FirstDelayMinutes is null)
        {
            await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
            await SendTrackedMessageAsync(botClient, chatId, session, "Сначала выбери время первого напоминания.", cancellationToken: cancellationToken);
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

        var reminder = await reminderService.ScheduleReminderAsync(
            user.Id,
            messageText,
            scheduledAt,
            repeatValue,
            templateId,
            cancellationToken);

        session.Reset();

        var nextTriggerLocal = reminder.NextTriggerAt.ToLocalTime();
        var repeatText = repeatValue.HasValue
            ? $" Повтор каждые {FormatInterval(repeatValue.Value)}."
            : " Без повтора.";

        await DeleteLastBotMessageAsync(botClient, chatId, session, cancellationToken);
        await SendTrackedMessageAsync(botClient, chatId, session,
            $"Готово! Напоминание \"{messageText}\" запланировано на {nextTriggerLocal:HH:mm}." + repeatText,
            replyMarkup: BuildBackToMenuKeyboard(),
            cancellationToken: cancellationToken);
    }

    private static InlineKeyboardMarkup BuildDelayKeyboard(string prefix, string code)
    {
        var rows = new List<List<InlineKeyboardButton>>
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
                InlineKeyboardButton.WithCallbackData("↩️ В меню", CallbackMenu)
            }
        };

        return new InlineKeyboardMarkup(rows);
    }

    private InlineKeyboardMarkup BuildRepeatKeyboard(string prefix, string code, int? defaultRepeat)
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
            InlineKeyboardButton.WithCallbackData("↩️ В меню", CallbackMenu)
        });

        return new InlineKeyboardMarkup(rows);
    }

    private InlineKeyboardMarkup BuildReminderListKeyboard(IReadOnlyList<Reminder> reminders)
    {
        var rows = new List<List<InlineKeyboardButton>>();

        foreach (var reminder in reminders)
        {
            rows.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData(
                    $"❌ {GetReminderDisplayName(reminder)}",
                    $"{CallbackRemindersDisable}:{reminder.Id:N}")
            });
        }

        rows.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("↩️ В меню", CallbackMenu)
        });

        return new InlineKeyboardMarkup(rows);
    }

    private async Task<Message> SendTrackedMessageAsync(ITelegramBotClient botClient, long chatId, ConversationContext session, string text, InlineKeyboardMarkup? replyMarkup = null, CancellationToken cancellationToken = default)
    {
        var message = await botClient.SendMessage(new ChatId(chatId), text, replyMarkup: replyMarkup, cancellationToken: cancellationToken);
        session.LastBotMessageId = message.MessageId;
        return message;
    }

    private async Task<bool> DeleteLastBotMessageAsync(ITelegramBotClient botClient, long chatId, ConversationContext session, CancellationToken cancellationToken)
    {
        if (session.LastBotMessageId is not int messageId)
        {
            return false;
        }

        try
        {
            await botClient.DeleteMessage(new ChatId(chatId), messageId, cancellationToken);
            session.LastBotMessageId = null;
            return true;
        }
        catch (ApiRequestException ex)
        {
            _logger.LogDebug(ex, "Не удалось удалить сообщение {MessageId} в чате {ChatId}", messageId, chatId);
            session.LastBotMessageId = null;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка при удалении сообщения {MessageId} в чате {ChatId}", messageId, chatId);
            return false;
        }
    }

    private static InlineKeyboardMarkup BuildBackToMenuKeyboard()
        => new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("↩️ В меню", CallbackMenu)
            }
        });

    private static string BuildReminderSummary(Reminder reminder, int index)
    {
        var title = reminder.Template?.Title ?? reminder.Message;
        var next = reminder.NextTriggerAt.ToLocalTime();
        var repeatPart = reminder.RepeatIntervalMinutes is { } interval and > 0
            ? $"повтор каждые {FormatInterval(interval)}"
            : "без повтора";

        return $"{index}. {title} — {next:dd.MM HH:mm}, {repeatPart}";
    }

    private static string GetReminderDisplayName(Reminder reminder)
    {
        var title = reminder.Template?.Title ?? reminder.Message;
        return title.Length > 32 ? title[..29] + "…" : title;
    }

    private static string FormatInterval(int minutes)
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

    private sealed class ConversationContext
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

    private enum ConversationFlow
    {
        None,
        Template,
        Custom
    }

    private enum ConversationStage
    {
        None,
        AwaitingCustomMessage,
        AwaitingFirstDelayMinutes,
        AwaitingRepeatMinutes
    }
}
