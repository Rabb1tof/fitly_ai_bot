using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthBot.Infrastructure.Data;
using HealthBot.Infrastructure.Telegram;
using HealthBot.Infrastructure.Telegram.Commands;
using HealthBot.Shared.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly IConversationContextStore _sessionStore;
    private readonly IRedisCacheService _cache;
    private readonly RedisOptions _redisOptions;

    public TelegramUpdateHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramUpdateHandler> logger,
        CommandDispatcher dispatcher,
        IConversationContextStore sessionStore,
        IRedisCacheService cache,
        IOptions<RedisOptions> redisOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _dispatcher = dispatcher;
        _sessionStore = sessionStore;
        _cache = cache;
        _redisOptions = redisOptions.Value;
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

        if (await IsRateLimitedAsync(RedisCacheKeys.RateLimitMessages(message.Chat.Id), _redisOptions.MessageRateLimitPerMinute, cancellationToken) is { IsLimited: true } rateLimit)
        {
            if (rateLimit.ShouldNotify)
            {
                await botClient.SendMessage(new ChatId(message.Chat.Id), "Слишком много запросов. Попробуй позже.", cancellationToken: cancellationToken);
            }

            _logger.LogWarning("Message rate limit exceeded for chat {ChatId} (count: {Count})", message.Chat.Id, rateLimit.Count);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var userService = services.GetRequiredService<UserService>();

        var username = message.From?.Username ?? message.Chat.Username;
        var user = await userService.RegisterUserAsync(message.Chat.Id, username, cancellationToken);
        var session = await _sessionStore.GetSessionAsync(message.Chat.Id, cancellationToken);

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

        try
        {
            if (!await _dispatcher.DispatchAsync(context))
            {
                await context.DeleteLastMessageAsync();
                await context.SendMessageAsync("Я пока не понимаю это сообщение. Используй /menu для управления напоминаниями.");
            }
        }
        finally
        {
            await PersistSessionAsync(message.Chat.Id, session, cancellationToken);
        }
    }

    private async Task HandleCallbackAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        var callbackQuery = update.CallbackQuery!;
        var chatId = callbackQuery.Message?.Chat.Id ?? callbackQuery.From.Id;

        if (await IsRateLimitedAsync(RedisCacheKeys.RateLimitCallbacks(chatId), _redisOptions.CallbackRateLimitPerMinute, cancellationToken) is { IsLimited: true } rateLimit)
        {
            if (rateLimit.ShouldNotify)
            {
                await botClient.AnswerCallbackQuery(callbackQuery.Id, "Слишком много действий. Попробуй позже.", showAlert: true, cancellationToken: cancellationToken);
            }

            _logger.LogWarning("Callback rate limit exceeded for chat {ChatId} (count: {Count})", chatId, rateLimit.Count);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var userService = services.GetRequiredService<UserService>();

        var username = callbackQuery.From.Username;
        var user = await userService.RegisterUserAsync(chatId, username, cancellationToken);
        var session = await _sessionStore.GetSessionAsync(chatId, cancellationToken);

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

        try
        {
            if (!await _dispatcher.DispatchAsync(context))
            {
                await context.AnswerCallbackAsync("Неизвестное действие");
            }
        }
        finally
        {
            await PersistSessionAsync(chatId, session, cancellationToken);
        }
    }

    private async Task PersistSessionAsync(long chatId, ConversationContext session, CancellationToken cancellationToken)
    {
        try
        {
            if (IsSessionEmpty(session))
            {
                await _sessionStore.DeleteSessionAsync(chatId, cancellationToken);
            }
            else
            {
                await _sessionStore.SaveSessionAsync(chatId, session, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось сохранить состояние сессии для чата {ChatId}", chatId);
        }
    }

    private static bool IsSessionEmpty(ConversationContext session)
        => session.Flow == ConversationFlow.None
           && session.Stage == ConversationStage.None
           && session.TemplateCode is null
           && session.TemplateId is null
           && session.TemplateTitle is null
           && session.TemplateDefaultRepeat is null
           && session.CustomMessage is null
           && session.FirstDelayMinutes is null
           && session.ExpectManualInput == false
           && session.LastBotMessageId is null;

    private async Task<(bool IsLimited, bool ShouldNotify, long Count)> IsRateLimitedAsync(string key, int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return (false, false, 0);
        }

        var window = _redisOptions.GetRateLimitWindow();
        if (window <= TimeSpan.Zero)
        {
            return (false, false, 0);
        }

        var count = await _cache.IncrementAsync(key, 1, window, cancellationToken);
        if (count <= limit)
        {
            return (false, false, count);
        }

        var shouldNotify = count == limit + 1;
        return (true, shouldNotify, count);
    }

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
