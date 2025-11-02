using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HealthBot.Core.Entities;
using HealthBot.Infrastructure.Data;
using HealthBot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;

namespace HealthBot.Tests;

public class TimeZoneHelperTests
{
    [Fact]
    public void Resolve_WhenIdIsNull_ReturnsUtc()
    {
        var tz = TimeZoneHelper.Resolve(null);

        tz.Id.Should().Be(TimeZoneInfo.Utc.Id);
    }

    [Fact]
    public void Resolve_WhenIdExists_ReturnsMatchingTimeZone()
    {
        var expected = TimeZoneInfo.Local.Id;

        var tz = TimeZoneHelper.Resolve(expected);

        tz.Id.Should().Be(expected);
    }

    [Theory]
    [InlineData("UTC+3", 3)]
    [InlineData("UTC-5", -5)]
    [InlineData("UTC", 0)]
    public void TryResolve_UtcOffsetFormats(string input, int expectedHoursOffset)
    {
        var result = TimeZoneHelper.TryResolve(input, out var tz);

        result.Should().BeTrue();
        tz.BaseUtcOffset.Should().Be(TimeSpan.FromHours(expectedHoursOffset));
    }

    [Fact]
    public void TryResolve_InvalidId_ReturnsFalse()
    {
        var result = TimeZoneHelper.TryResolve("Invalid/Zone", out var tz);

        result.Should().BeFalse();
        tz.Should().Be(TimeZoneInfo.Utc);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ConvertUtcToUserTime_SupportsDifferentKinds(DateTimeKind kind)
    {
        var utc = new DateTime(2025, 11, 2, 12, 0, 0, kind);
        if (utc.Kind == DateTimeKind.Local)
        {
            utc = utc.ToUniversalTime();
        }

        var tz = TimeZoneHelper.Resolve("UTC+3");

        var local = TimeZoneHelper.ConvertUtcToUserTime(utc, tz);

        local.Should().Be(new DateTime(2025, 11, 2, 15, 0, 0));
    }
}

public class TelegramUpdateHandlerTests
{
    private const long ChatId = 12345;

    [Fact]
    public async Task HandleMessageAsync_Start_ShouldSendMainMenu()
    {
        await using var harness = new HandlerHarness();

        await harness.SendTextAsync("/start");

        harness.SentMessages.Should().HaveCount(1);
        var (text, markup) = harness.SentMessages.Single();
        text.Should().Contain("Привет");
        markup.Should().NotBeNull();

        var callbackData = markup!.InlineKeyboard
            .SelectMany(row => row)
            .Select(button => button.CallbackData)
            .ToList();

        callbackData.Should().Contain(new[] { "main_reminders", "main_nutrition", "main_settings" });
    }

    [Fact]
    public async Task HandleCallbackAsync_SettingsTimezone_ShouldShowOptions()
    {
        await using var harness = new HandlerHarness();
        await harness.SendTextAsync("/start");
        harness.SentMessages.Clear();

        await harness.SendCallbackAsync("main_settings");
        harness.SentMessages.Clear();

        await harness.SendCallbackAsync("settings_timezone");

        harness.SentMessages.Should().HaveCount(1);
        var (text, markup) = harness.SentMessages.Single();
        text.Should().Contain("Выбери таймзону");
        markup.Should().NotBeNull();
        markup!.InlineKeyboard.SelectMany(x => x).Select(b => b.Text)
            .Should().Contain("Europe/Moscow");
    }

    [Fact]
    public async Task HandleCallbackAsync_SelectTimezone_ShouldPersistAndNotify()
    {
        await using var harness = new HandlerHarness();
        await harness.SendTextAsync("/start");
        harness.SentMessages.Clear();

        await harness.SendCallbackAsync("main_settings");
        harness.SentMessages.Clear();

        await harness.SendCallbackAsync("settings_timezone_select:Europe/Kyiv");

        harness.SentMessages.Should().ContainSingle();
        harness.SentMessages.Single().Text.Should().Contain("Таймзона обновлена");

        var storedUser = await harness.DbContext.Users.AsNoTracking().SingleAsync();
        storedUser.TimeZoneId.Should().Be("Europe/Kyiv");
    }

    [Fact]
    public async Task ManualTimezoneInput_ShouldUpdateUser()
    {
        await using var harness = new HandlerHarness();
        await harness.SendTextAsync("/start");
        harness.SentMessages.Clear();

        await harness.SendCallbackAsync("main_settings");
        harness.SentMessages.Clear();

        await harness.SendCallbackAsync("settings_timezone_manual");
        harness.SentMessages.Should().ContainSingle();
        harness.SentMessages.Single().Text.Should().Contain("Введи идентификатор таймзоны");
        harness.SentMessages.Clear();

        await harness.SendTextAsync("UTC+2");

        harness.SentMessages.Should().ContainSingle();
        harness.SentMessages.Single().Text.Should().Contain("Таймзона обновлена");

        var storedUser = await harness.DbContext.Users.AsNoTracking().SingleAsync();
        storedUser.TimeZoneId.Should().Be("UTC+2");
    }

    [Fact]
    public async Task ReminderList_ShouldUseUserTimeZone()
    {
        await using var harness = new HandlerHarness();
        await harness.SendTextAsync("/start");

        var user = await harness.DbContext.Users.SingleAsync();
        user.TimeZoneId = "UTC+3";

        harness.DbContext.Reminders.Add(new Reminder
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            Message = "Выпить воду",
            ScheduledAt = DateTime.UtcNow,
            NextTriggerAt = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            IsActive = true
        });
        await harness.DbContext.SaveChangesAsync();

        harness.SentMessages.Clear();
        await harness.SendCallbackAsync("main_reminders");
        harness.SentMessages.Clear();

        await harness.SendCallbackAsync("reminders_list");

        harness.SentMessages.Should().ContainSingle();
        var (text, markup) = harness.SentMessages.Single();
        text.Should().Contain("15:00");
        markup!.InlineKeyboard.Last().Single().Text.Should().Be("↩️ К напоминаниям");
    }

    private sealed class HandlerHarness : IAsyncDisposable
    {
        private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
        private readonly Mock<IServiceScope> _scopeMock;
        private readonly Mock<IServiceProvider> _providerMock;
        private readonly Mock<ILogger<TelegramUpdateHandler>> _loggerMock = new();

        public HealthBotDbContext DbContext { get; }
        public UserService UserService { get; }
        public ReminderService ReminderService { get; }
        private readonly Mock<ITelegramBotClient> _botMock = new();
        public TestTelegramUpdateHandler Handler { get; }
        public List<(string Text, InlineKeyboardMarkup? Markup)> SentMessages { get; } = new();

        public HandlerHarness()
        {
            var services = new ServiceCollection();
            services.AddDbContext<HealthBotDbContext>(options =>
                options.UseInMemoryDatabase(Guid.NewGuid().ToString()));

            var provider = services.BuildServiceProvider();
            DbContext = provider.GetRequiredService<HealthBotDbContext>();
            UserService = new UserService(DbContext);
            ReminderService = new ReminderService(DbContext);

            _providerMock = new Mock<IServiceProvider>();
            _providerMock.Setup(p => p.GetService(typeof(UserService))).Returns(UserService);
            _providerMock.Setup(p => p.GetService(typeof(ReminderService))).Returns(ReminderService);

            _scopeMock = new Mock<IServiceScope>();
            _scopeMock.SetupGet(s => s.ServiceProvider).Returns(_providerMock.Object);

            _scopeFactoryMock = new Mock<IServiceScopeFactory>();
            _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(_scopeMock.Object);

            Handler = new TestTelegramUpdateHandler(_scopeFactoryMock.Object, _loggerMock.Object, SentMessages);
        }

        public Task SendTextAsync(string text)
        {
            var update = TestUpdates.CreateMessageUpdate(ChatId, text);
            return Handler.HandleUpdateAsync(_botMock.Object, update, CancellationToken.None);
        }

        public Task SendCallbackAsync(string data)
        {
            var update = TestUpdates.CreateCallbackUpdate(ChatId, data);
            return Handler.HandleUpdateAsync(_botMock.Object, update, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.Database.EnsureDeletedAsync();
            await DbContext.DisposeAsync();
        }
    }

    private sealed class TestTelegramUpdateHandler : TelegramUpdateHandler
    {
        private readonly List<(string Text, InlineKeyboardMarkup? Markup)> _sentMessages;
        private int _messageId;

        public TestTelegramUpdateHandler(IServiceScopeFactory scopeFactory, ILogger<TelegramUpdateHandler> logger, List<(string Text, InlineKeyboardMarkup? Markup)> sentMessages)
            : base(scopeFactory, logger)
        {
            _sentMessages = sentMessages;
        }

        protected override Task<Message> SendTrackedMessageAsync(ITelegramBotClient botClient, long chatId, ConversationContext session, string text, InlineKeyboardMarkup? replyMarkup = null, CancellationToken cancellationToken = default)
        {
            var message = TestUpdates.CreateMessage(++_messageId, new ChatId(chatId));
            session.LastBotMessageId = message.MessageId;
            _sentMessages.Add((text, replyMarkup));
            return Task.FromResult(message);
        }

        protected override Task<bool> DeleteLastBotMessageAsync(ITelegramBotClient botClient, long chatId, ConversationContext session, CancellationToken cancellationToken)
        {
            if (session.LastBotMessageId is null)
            {
                return Task.FromResult(false);
            }

            session.LastBotMessageId = null;
            return Task.FromResult(true);
        }
    }

    private static class TestUpdates
    {
        public static Update CreateMessageUpdate(long chatId, string text)
        {
            var payload = new
            {
                update_id = 1,
                message = new
                {
                    message_id = 10,
                    date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    chat = new { id = chatId, type = "private" },
                    text,
                    from = new { id = chatId, is_bot = false, first_name = "Test", username = "tester" }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            return DeserializeUpdate(json);
        }

        public static Update CreateCallbackUpdate(long chatId, string data)
        {
            var payload = new
            {
                update_id = 1,
                callback_query = new
                {
                    id = Guid.NewGuid().ToString("N"),
                    data,
                    from = new { id = chatId, is_bot = false, first_name = "Test", username = "tester" },
                    message = new
                    {
                        message_id = 20,
                        date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        chat = new { id = chatId, type = "private" }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            return DeserializeUpdate(json);
        }

        private static Update DeserializeUpdate(string json)
        {
            var update = JsonSerializer.Deserialize<Update>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (update is null)
            {
                throw new InvalidOperationException("Failed to deserialize update");
            }

            return update;
        }

        public static Message CreateMessage(int id, ChatId chatId)
        {
            var payload = new
            {
                message_id = id,
                date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                chat = new { id = chatId.Identifier ?? ChatId, type = "private" }
            };

            var json = JsonSerializer.Serialize(payload);
            var message = JsonSerializer.Deserialize<Message>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Failed to deserialize message");

            return message;
        }
    }
}
