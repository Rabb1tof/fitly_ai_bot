using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthBot.Core.Entities;
using HealthBot.Infrastructure.Telegram;
using HealthBot.Infrastructure.Telegram.Commands;
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
    private readonly CommandDispatcher _dispatcher;
    private readonly ConcurrentDictionary<long, ConversationContext> _sessions = new();

    public TelegramUpdateHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramUpdateHandler> logger,
        CommandDispatcher dispatcher)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _dispatcher = dispatcher;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            switch (update.Type)
            {
                case UpdateType.Message when update.Message is not null:
                    await HandleMessageAsync(botClient, update, cancellationToken);
                    break;
                case UpdateType.CallbackQuery when update.CallbackQuery is not null:
                    await HandleCallbackAsync(botClient, update, cancellationToken);
                    break;
                default:
                    _logger.LogDebug("Unsupported update type {Type}", update.Type);
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

    private async Task HandleMessageAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        var message = update.Message!;
        if (message.Chat.Type is not ChatType.Private)
        {
            _logger.LogDebug("Ignoring non-private chat {ChatId}", message.Chat.Id);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var userService = services.GetRequiredService<UserService>();

        var username = message.From?.Username ?? message.Chat.Username;
        var user = await userService.RegisterUserAsync(message.Chat.Id, username, cancellationToken);
        var session = GetSession(message.Chat.Id);

        var context = new CommandContext(
            botClient,
            update,
            message.Chat.Id,
            session,
            user,
            services,
            cancellationToken,
            SendTrackedMessageAsync,
            DeleteLastBotMessageAsync);

        if (!await _dispatcher.DispatchAsync(context))
        {
            await context.DeleteLastMessageAsync();
            await context.SendMessageAsync("Я пока не понимаю это сообщение. Используй /menu для управления напоминаниями.");
        }
    }

    private async Task HandleCallbackAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        var callbackQuery = update.CallbackQuery!;
        var chatId = callbackQuery.Message?.Chat.Id ?? callbackQuery.From.Id;

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var userService = services.GetRequiredService<UserService>();

        var username = callbackQuery.From.Username;
        var user = await userService.RegisterUserAsync(chatId, username, cancellationToken);
        var session = GetSession(chatId);

        var context = new CommandContext(
            botClient,
            update,
            chatId,
            session,
            user,
            services,
            cancellationToken,
            SendTrackedMessageAsync,
            DeleteLastBotMessageAsync);

        if (!await _dispatcher.DispatchAsync(context))
        {
            await context.AnswerCallbackAsync("Неизвестное действие");
        }
    }

    private ConversationContext GetSession(long chatId)
        => _sessions.GetOrAdd(chatId, _ => new ConversationContext());

    protected virtual async Task<Message> SendTrackedMessageAsync(ITelegramBotClient botClient, long chatId, ConversationContext session, string text, InlineKeyboardMarkup? replyMarkup = null, CancellationToken cancellationToken = default)
    {
        var message = await botClient.SendMessage(new ChatId(chatId), text, replyMarkup: replyMarkup, cancellationToken: cancellationToken);
        session.LastBotMessageId = message.MessageId;
        return message;
    }

    protected virtual async Task<bool> DeleteLastBotMessageAsync(ITelegramBotClient botClient, long chatId, ConversationContext session, CancellationToken cancellationToken)
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
}
