using System;
using System.Threading;
using System.Threading.Tasks;
using CoreUser = HealthBot.Core.Entities.User;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace HealthBot.Infrastructure.Telegram;

public sealed class CommandContext
{
    public CommandContext(
        ITelegramBotClient botClient,
        Update update,
        long chatId,
        ConversationContext session,
        CoreUser user,
        IServiceProvider services,
        CancellationToken cancellationToken,
        Func<ITelegramBotClient, long, ConversationContext, string, InlineKeyboardMarkup?, CancellationToken, Task<Message>> sendMessage,
        Func<ITelegramBotClient, long, ConversationContext, CancellationToken, Task<bool>> deleteMessage)
    {
        BotClient = botClient;
        Update = update;
        ChatId = chatId;
        Session = session;
        User = user;
        Services = services;
        CancellationToken = cancellationToken;
        _sendMessage = sendMessage;
        _deleteMessage = deleteMessage;
    }

    public ITelegramBotClient BotClient { get; }
    public Update Update { get; }
    public long ChatId { get; }
    public ConversationContext Session { get; }
    public CoreUser User { get; }
    public IServiceProvider Services { get; }
    public CancellationToken CancellationToken { get; }

    public Message? Message => Update.Message;
    public CallbackQuery? CallbackQuery => Update.CallbackQuery;
    public bool HasCallback => CallbackQuery is not null;
    public bool CallbackAnswered => _callbackAnswered;

    private readonly Func<ITelegramBotClient, long, ConversationContext, string, InlineKeyboardMarkup?, CancellationToken, Task<Message>> _sendMessage;
    private readonly Func<ITelegramBotClient, long, ConversationContext, CancellationToken, Task<bool>> _deleteMessage;
    private bool _callbackAnswered;

    public Task<Message> SendMessageAsync(string text, InlineKeyboardMarkup? replyMarkup = null)
        => _sendMessage(BotClient, ChatId, Session, text, replyMarkup, CancellationToken);

    public Task<bool> DeleteLastMessageAsync()
        => _deleteMessage(BotClient, ChatId, Session, CancellationToken);

    public async Task AnswerCallbackAsync(string? text = null, bool showAlert = false, string? url = null, int cacheTime = 0)
    {
        if (CallbackQuery is null)
        {
            return;
        }

        await BotClient.AnswerCallbackQuery(CallbackQuery.Id, text, showAlert, url, cacheTime, CancellationToken);
        _callbackAnswered = true;
    }
}
