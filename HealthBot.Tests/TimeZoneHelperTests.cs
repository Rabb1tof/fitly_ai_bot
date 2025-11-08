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
using HealthBot.Infrastructure.Telegram;
using HealthBot.Infrastructure.Telegram.Commands;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Callback;
using HealthBot.Infrastructure.Telegram.Commands.Message;
using HealthBot.Shared.Options;
using HealthBot.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;
using CoreUser = HealthBot.Core.Entities.User;

namespace HealthBot.Tests;

public class TimeZoneHelperTests
{
    private const int DefaultTtlMinutes = 10;
    private const string CoverageReminderMessage = "К покрытию";

    [Fact]
    public void Resolve_WhenIdIsNull_ReturnsUtc()
    {
        var tz = TimeZoneHelper.Resolve(null);

        tz.Id.Should().Be(TimeZoneInfo.Utc.Id);
    }

    private static HealthBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HealthBotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new HealthBotDbContext(options);
    }

    [Fact]
    public async Task UserService_CachesUserProfileInRedis()
    {
        await using var dbContext = CreateDbContext();
        var cache = new InMemoryRedisCacheService();
        var redisOptions = Options.Create(new RedisOptions { ConnectionString = "localhost", DefaultTtlMinutes = DefaultTtlMinutes });

        var service = new UserService(dbContext, cache, redisOptions);

        var user = await service.RegisterUserAsync(42, "tester");

        cache.TryGetRaw(RedisCacheKeys.UserProfile(42), out var cached).Should().BeTrue();
        cached.Should().BeOfType<CoreUser>().Which.Username.Should().Be(user.Username);
    }

    [Fact]
    public async Task UserService_UpdateUsername_RefreshesCache()
    {
        await using var dbContext = CreateDbContext();
        var cache = new InMemoryRedisCacheService();
        var redisOptions = Options.Create(new RedisOptions { ConnectionString = "localhost", DefaultTtlMinutes = 10 });

        var service = new UserService(dbContext, cache, redisOptions);

        var initial = await service.RegisterUserAsync(100, "old");
        initial.Username.Should().Be("old");

        var updated = await service.RegisterUserAsync(100, "new");

        updated.Username.Should().Be("new");
        cache.TryGetRaw(RedisCacheKeys.UserProfile(100), out var cached).Should().BeTrue();
        cached.Should().BeOfType<CoreUser>().Which.Username.Should().Be("new");
    }

    [Fact]
    public async Task ReminderService_SchedulesReminder_AddsToQueueAndDequeues()
    {
        await using var dbContext = CreateDbContext();
        var cache = new InMemoryRedisCacheService();
        var redisOptions = Options.Create(new RedisOptions
        {
            ConnectionString = "localhost",
            ReminderBatchSize = 10,
            ReminderLockSeconds = 30,
            ReminderLookaheadMinutes = 5
        });

        var service = new ReminderService(dbContext, cache, redisOptions);

        var user = new CoreUser
        {
            Id = Guid.NewGuid(),
            TelegramId = 100,
            Username = "tester"
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var scheduledAt = DateTime.UtcNow.AddSeconds(-5);
        var reminder = await service.ScheduleReminderAsync(user.Id, CoverageReminderMessage, scheduledAt);

        var queueSnapshot = cache.GetSortedSetSnapshot(RedisCacheKeys.ReminderQueue());
        queueSnapshot.Should().ContainKey(reminder.Id.ToString("N"));

        var now = DateTime.UtcNow;
        var leases = await service.DequeueDueRemindersAsync(now, now.AddMinutes(1), CancellationToken.None);
        leases.Should().HaveCount(1);
        leases[0].Reminder.Id.Should().Be(reminder.Id);

        await service.MarkAsSentAsync(new[] { leases[0].Reminder }, DateTime.UtcNow, CancellationToken.None);
        await service.ReleaseReminderLockAsync(reminder.Id, leases[0].LockValue, CancellationToken.None);

        cache.GetSortedSetSnapshot(RedisCacheKeys.ReminderQueue()).Should().NotContainKey(reminder.Id.ToString("N"));
    }

    [Fact]
    public async Task ReminderService_FutureReminder_RemainsInQueueUntilDue()
    {
        await using var dbContext = CreateDbContext();
        var cache = new InMemoryRedisCacheService();
        var redisOptions = Options.Create(new RedisOptions
        {
            ConnectionString = "localhost",
            ReminderBatchSize = 5,
            ReminderLockSeconds = 30,
            ReminderLookaheadMinutes = 5
        });

        var service = new ReminderService(dbContext, cache, redisOptions);
        var user = new CoreUser { Id = Guid.NewGuid(), TelegramId = 200, Username = "tester" };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var scheduledAt = DateTime.UtcNow.AddMinutes(2);
        var reminder = await service.ScheduleReminderAsync(user.Id, CoverageReminderMessage, scheduledAt);

        var now = DateTime.UtcNow;
        var leases = await service.DequeueDueRemindersAsync(now, now.AddMinutes(3), CancellationToken.None);

        leases.Should().BeEmpty();
        cache.GetSortedSetSnapshot(RedisCacheKeys.ReminderQueue()).Should().ContainKey(reminder.Id.ToString("N"));
    }

    [Fact]
    public async Task ReminderService_RestoresQueueFromDatabase_WhenRedisQueueIsEmpty()
    {
        await using var dbContext = CreateDbContext();
        var cache = new InMemoryRedisCacheService();
        var redisOptions = Options.Create(new RedisOptions
        {
            ConnectionString = "localhost",
            ReminderBatchSize = 5,
            ReminderLockSeconds = 30,
            ReminderLookaheadMinutes = 5,
            ReminderQueueRecoveryWindowMinutes = 60
        });

        var service = new ReminderService(dbContext, cache, redisOptions);
        var user = new CoreUser { Id = Guid.NewGuid(), TelegramId = 400, Username = "tester" };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var scheduledAt = DateTime.UtcNow.AddSeconds(-10);
        var reminder = await service.ScheduleReminderAsync(user.Id, CoverageReminderMessage, scheduledAt);

        // Эмулируем очистку Redis-очереди.
        await cache.RemoveRangeByScoreAsync(RedisCacheKeys.ReminderQueue(), double.NegativeInfinity, double.PositiveInfinity);

        var now = DateTime.UtcNow;
        var leases = await service.DequeueDueRemindersAsync(now, now.AddMinutes(1), CancellationToken.None);

        leases.Should().HaveCount(1);
        leases[0].Reminder.Id.Should().Be(reminder.Id);
        leases[0].LockValue.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReminderService_DequeueSkipsReminderWhenLockIsHeld()
    {
        await using var dbContext = CreateDbContext();
        var cache = new InMemoryRedisCacheService();
        var redisOptions = Options.Create(new RedisOptions
        {
            ConnectionString = "localhost",
            ReminderBatchSize = 5,
            ReminderLockSeconds = 30,
            ReminderLookaheadMinutes = 5
        });

        var service = new ReminderService(dbContext, cache, redisOptions);
        var user = new CoreUser { Id = Guid.NewGuid(), TelegramId = 300, Username = "tester" };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var scheduledAt = DateTime.UtcNow.AddSeconds(-5);
        var reminder = await service.ScheduleReminderAsync(user.Id, CoverageReminderMessage, scheduledAt);

        await cache.AcquireLockAsync(RedisCacheKeys.ReminderLock(reminder.Id), "lock", TimeSpan.FromMinutes(1));

        var now = DateTime.UtcNow;
        var leases = await service.DequeueDueRemindersAsync(now, now.AddMinutes(1), CancellationToken.None);

        leases.Should().BeEmpty();
        cache.GetSortedSetSnapshot(RedisCacheKeys.ReminderQueue()).Should().ContainKey(reminder.Id.ToString("N"));
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
    public async Task StartMessage_ShowsMainMenu()
    {
        await using var harness = new HandlerHarness();

        await harness.SendTextAsync("/start");

        harness.SentMessages.Should().HaveCount(1);
        var (text, markup) = harness.SentMessages.Single();
        text.Should().Contain("Привет");
        markup.Should().NotBeNull();
        markup!.InlineKeyboard.SelectMany(row => row.Select(button => button.CallbackData))
            .Should().Contain(new[]
            {
                TelegramCommandNames.CallbackMainReminders,
                TelegramCommandNames.CallbackMainNutrition,
                TelegramCommandNames.CallbackMainSettings
            });
    }

    [Fact]
    public async Task SettingsTimezone_ShowsPopularZones()
    {
        await using var harness = new HandlerHarness();

        await harness.SendTextAsync("/start");
        harness.ClearMessages();

        await harness.SendCallbackAsync(TelegramCommandNames.CallbackMainSettings);
        harness.ClearMessages();

        await harness.SendCallbackAsync(TelegramCommandNames.CallbackSettingsTimezone);

        harness.SentMessages.Should().ContainSingle();
        var (text, markup) = harness.SentMessages.Single();
        text.Should().Contain("Выбери таймзону");
        markup!.InlineKeyboard.SelectMany(x => x.Select(b => b.Text)).Should().Contain("Europe/Moscow");
    }

    [Fact]
    public async Task ManualTimezone_ValidValue_Persists()
    {
        await using var harness = new HandlerHarness();

        await harness.SendTextAsync("/start");
        harness.ClearMessages();

        await harness.SendCallbackAsync(TelegramCommandNames.CallbackMainSettings);
        harness.ClearMessages();

        await harness.SendCallbackAsync(TelegramCommandNames.CallbackSettingsTimezoneManual);
        harness.SentMessages.Should().ContainSingle();
        harness.ClearMessages();

        await harness.SendTextAsync("UTC+2");

        harness.SentMessages.Should().ContainSingle();
        harness.SentMessages.Single().Text.Should().Contain("Таймзона обновлена");

        await harness.EnsureUserAsync(ChatId);
        var storedUser = await harness.QueryDbContextAsync(ctx => ctx.Users.AsNoTracking().FirstOrDefaultAsync());
        storedUser.Should().NotBeNull();
        storedUser!.TimeZoneId.Should().Be("UTC+2");
    }

    [Fact]
    public async Task ReminderList_FormatsWithUserTimezone()
    {
        await using var harness = new HandlerHarness();

        await harness.SendTextAsync("/start");

        await harness.EnsureUserAsync(ChatId);

        await harness.QueryDbContextAsync(async ctx =>
        {
            var user = await ctx.Users.SingleAsync();
            user.TimeZoneId = "UTC+3";

            ctx.Reminders.Add(new Reminder
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                User = user,
                Message = "Выпить воду",
                ScheduledAt = DateTime.UtcNow,
                NextTriggerAt = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                IsActive = true
            });
            await ctx.SaveChangesAsync();
        });

        harness.ClearMessages();
        await harness.SendCallbackAsync(TelegramCommandNames.CallbackMainReminders);
        harness.ClearMessages();

        await harness.SendCallbackAsync(TelegramCommandNames.CallbackRemindersList);

        harness.SentMessages.Should().ContainSingle();
        var (text, markup) = harness.SentMessages.Single();
        text.Should().Contain("15:00");
        markup!.InlineKeyboard.Last().Single().Text.Should().Be("↩️ К напоминаниям");
    }

    [Fact]
    public async Task TemplateFlow_SchedulesReminder()
    {
        await using var harness = new HandlerHarness();
        await harness.SendTextAsync("/start");

        harness.DbContext.ReminderTemplates.Add(new ReminderTemplate
        {
            Id = Guid.NewGuid(),
            Code = "drink",
            Title = "Попей воды",
            Description = string.Empty,
            DefaultRepeatIntervalMinutes = null,
            IsSystem = true
        });
        await harness.DbContext.SaveChangesAsync();

        await harness.SendCallbackAsync(TelegramCommandNames.CallbackMainReminders);
        harness.ClearMessages();

        await harness.SendCallbackAsync(TelegramCommandNames.CallbackRemindersTemplates);
        harness.SentMessages.Single().Text.Should().Contain("Выбери шаблон");

        await harness.SendCallbackAsync($"{TelegramCommandNames.CallbackTemplateSelect}:drink");
        harness.SentMessages.Last().Text.Should().Contain("Через сколько минут");

        await harness.SendCallbackAsync($"{TelegramCommandNames.CallbackTemplateDelay}:drink:15");
        harness.SentMessages.Last().Text.Should().Contain("Как часто повторять");

        await harness.SendCallbackAsync($"{TelegramCommandNames.CallbackTemplateRepeat}:drink:0");
        harness.SentMessages.Last().Text.Should().Contain("Готово!");

        var reminder = await harness.DbContext.Reminders.SingleAsync();
        reminder.Message.Should().Be("Попей воды");
        reminder.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task MessageRateLimit_StopsSecondMessageWithinWindow()
    {
        var redisOptions = new RedisOptions
        {
            ConnectionString = "localhost",
            MessageRateLimitPerMinute = 1,
            RateLimitWindowSeconds = 60
        };

        var cache = new InMemoryRedisCacheService();

        await using var harness = new HandlerHarness(cache, redisOptions);

        await harness.SendTextAsync("/start");
        harness.SentMessages.Should().NotBeEmpty();

        var initialCount = harness.SentMessages.Count;
        await harness.SendTextAsync("/menu");

        harness.SentMessages.Should().HaveCount(initialCount);

        cache.TryGetRaw(RedisCacheKeys.RateLimitMessages(ChatId), out var rawCount).Should().BeTrue();
        rawCount.Should().BeOfType<long>().Which.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task CallbackRateLimit_StopsSecondCallbackWithinWindow()
    {
        var redisOptions = new RedisOptions
        {
            ConnectionString = "localhost",
            CallbackRateLimitPerMinute = 1,
            RateLimitWindowSeconds = 60
        };

        var cache = new InMemoryRedisCacheService();

        await using var harness = new HandlerHarness(cache, redisOptions);

        await harness.SendTextAsync("/start");
        harness.ClearMessages();

        await harness.SendCallbackAsync(TelegramCommandNames.CallbackMainReminders);
        var initialCount = harness.SentMessages.Count;

        await harness.SendCallbackAsync(TelegramCommandNames.CallbackMainReminders);

        harness.SentMessages.Should().HaveCount(initialCount);
        cache.TryGetRaw(RedisCacheKeys.RateLimitCallbacks(ChatId), out var rawCount).Should().BeTrue();
        rawCount.Should().BeOfType<long>().Which.Should().BeGreaterThan(1);
    }

    private sealed class HandlerHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ITelegramCommandHandler[] _handlers;
        private readonly Mock<ITelegramBotClient> _botMock = new();
        private readonly IRedisCacheService _redisCache;
        private readonly RedisOptions _redisOptions;

        public HealthBotDbContext DbContext { get; }
        public TelegramUpdateHandler Handler { get; }
        public List<(string Text, InlineKeyboardMarkup? Markup)> SentMessages { get; } = new();

        public Mock<ITelegramBotClient> BotMock => _botMock;
        public IRedisCacheService RedisCache => _redisCache;
        public RedisOptions RedisOptions => _redisOptions;

        public HandlerHarness(IRedisCacheService? redisCache = null, RedisOptions? redisOptions = null)
        {
            var dbOptions = new DbContextOptionsBuilder<HealthBotDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            DbContext = new HealthBotDbContext(dbOptions);

            var services = new ServiceCollection();
            _redisCache = redisCache ?? new NoOpRedisCacheService();
            _redisOptions = redisOptions ?? new RedisOptions();
            services.AddSingleton(DbContext);
            services.AddSingleton<IRedisCacheService>(_redisCache);
            services.AddSingleton<IOptions<RedisOptions>>(_ => Options.Create(_redisOptions));
            services.AddScoped<UserService>();
            services.AddScoped<ReminderService>();

            _handlers = CreateHandlers();
            var dispatcher = new CommandDispatcher(_handlers, NullLogger<CommandDispatcher>.Instance);

            _serviceProvider = services.BuildServiceProvider();
            _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

            Handler = new TestTelegramUpdateHandler(
                _scopeFactory,
                NullLogger<TelegramUpdateHandler>.Instance,
                dispatcher,
                new InMemoryConversationContextStore(Options.Create(_redisOptions)),
                _serviceProvider.GetRequiredService<IRedisCacheService>(),
                _serviceProvider.GetRequiredService<IOptions<RedisOptions>>(),
                SentMessages);
        }

        public async Task SendTextAsync(string text)
        {
            var update = UpdateFactory.CreateMessageUpdate(ChatId, text);
            await Handler.HandleUpdateAsync(_botMock.Object, update, CancellationToken.None);
        }

        public async Task SendCallbackAsync(string data)
        {
            var update = UpdateFactory.CreateCallbackUpdate(ChatId, data);
            await Handler.HandleUpdateAsync(_botMock.Object, update, CancellationToken.None);
        }

        public void ClearMessages() => SentMessages.Clear();

        public async ValueTask DisposeAsync()
        {
            await DbContext.Database.EnsureDeletedAsync();
            await DbContext.DisposeAsync();
            await _serviceProvider.DisposeAsync();
        }

        public Task<TResult> QueryDbContextAsync<TResult>(Func<HealthBotDbContext, Task<TResult>> query)
            => query(DbContext);

        public Task QueryDbContextAsync(Func<HealthBotDbContext, Task> action)
            => action(DbContext);

        public Task EnsureUserAsync(long chatId, string? username = "tester")
            => QueryDbContextAsync(async ctx =>
            {
                if (!await ctx.Users.AnyAsync(u => u.TelegramId == chatId))
                {
                    ctx.Users.Add(new CoreUser
                    {
                        TelegramId = chatId,
                        Username = username
                    });
                    await ctx.SaveChangesAsync();
                }
            });

        private static ITelegramCommandHandler[] CreateHandlers() => new ITelegramCommandHandler[]
        {
            new StartCommandHandler(NullLogger<StartCommandHandler>.Instance),
            new CancelCommandHandler(NullLogger<CancelCommandHandler>.Instance),
            new ManualInputMessageHandler(NullLogger<ManualInputMessageHandler>.Instance),
            new UnknownMessageHandler(NullLogger<UnknownMessageHandler>.Instance),
            new MenuCallbackHandler(NullLogger<MenuCallbackHandler>.Instance),
            new MainRemindersCallbackHandler(NullLogger<MainRemindersCallbackHandler>.Instance),
            new MainNutritionCallbackHandler(NullLogger<MainNutritionCallbackHandler>.Instance),
            new MainSettingsCallbackHandler(NullLogger<MainSettingsCallbackHandler>.Instance),
            new RemindersListCallbackHandler(NullLogger<RemindersListCallbackHandler>.Instance),
            new RemindersTemplatesCallbackHandler(NullLogger<RemindersTemplatesCallbackHandler>.Instance),
            new CustomNewCallbackHandler(NullLogger<CustomNewCallbackHandler>.Instance),
            new CustomDelayCallbackHandler(NullLogger<CustomDelayCallbackHandler>.Instance),
            new CustomRepeatCallbackHandler(NullLogger<CustomRepeatCallbackHandler>.Instance),
            new TemplateSelectCallbackHandler(NullLogger<TemplateSelectCallbackHandler>.Instance),
            new TemplateDelayCallbackHandler(NullLogger<TemplateDelayCallbackHandler>.Instance),
            new TemplateRepeatCallbackHandler(NullLogger<TemplateRepeatCallbackHandler>.Instance),
            new ReminderDisableCallbackHandler(NullLogger<ReminderDisableCallbackHandler>.Instance),
            new SettingsTimezoneCallbackHandler(NullLogger<SettingsTimezoneCallbackHandler>.Instance),
            new SettingsTimezoneSelectCallbackHandler(NullLogger<SettingsTimezoneSelectCallbackHandler>.Instance),
            new SettingsTimezoneManualCallbackHandler(NullLogger<SettingsTimezoneManualCallbackHandler>.Instance)
        };
    }

    private sealed class TestTelegramUpdateHandler : TelegramUpdateHandler
    {
        private readonly List<(string Text, InlineKeyboardMarkup? Markup)> _sentMessages;
        private int _messageId;

        public TestTelegramUpdateHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<TelegramUpdateHandler> logger,
            CommandDispatcher dispatcher,
            IConversationContextStore sessionStore,
            IRedisCacheService cache,
            IOptions<RedisOptions> redisOptions,
            List<(string Text, InlineKeyboardMarkup? Markup)> sentMessages)
            : base(scopeFactory, logger, dispatcher, sessionStore, cache, redisOptions)
        {
            _sentMessages = sentMessages;
        }

        protected override Task<Message> SendTrackedMessageAsync(
            ITelegramBotClient botClient,
            long chatId,
            ConversationContext session,
            string text,
            InlineKeyboardMarkup? replyMarkup = null,
            CancellationToken cancellationToken = default)
        {
            var message = UpdateFactory.CreateMessage(++_messageId, chatId);
            session.LastBotMessageId = message.MessageId;
            _sentMessages.Add((text, replyMarkup));
            return Task.FromResult(message);
        }

        protected override Task<bool> DeleteLastBotMessageAsync(
            ITelegramBotClient botClient,
            long chatId,
            ConversationContext session,
            CancellationToken cancellationToken)
        {
            session.LastBotMessageId = null;
            return Task.FromResult(true);
        }
    }

    private static class UpdateFactory
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public static Update CreateMessageUpdate(long chatId, string text)
        {
            var payload = new
            {
                update_id = Random.Shared.Next(1, int.MaxValue),
                message = new
                {
                    message_id = Random.Shared.Next(1, int.MaxValue),
                    date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    chat = new { id = chatId, type = "private" },
                    text,
                    from = new { id = chatId, is_bot = false, first_name = "Test", username = "tester" }
                }
            };

            return DeserializeUpdate(payload);
        }

        public static Update CreateCallbackUpdate(long chatId, string data)
        {
            var payload = new
            {
                update_id = Random.Shared.Next(1, int.MaxValue),
                callback_query = new
                {
                    id = Guid.NewGuid().ToString("N"),
                    data,
                    from = new { id = chatId, is_bot = false, first_name = "Test", username = "tester" },
                    message = new
                    {
                        message_id = Random.Shared.Next(1, int.MaxValue),
                        date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        chat = new { id = chatId, type = "private" }
                    }
                }
            };

            return DeserializeUpdate(payload);
        }

        public static Message CreateMessage(int messageId, long chatId)
        {
            var payload = new
            {
                message_id = messageId,
                date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                chat = new { id = chatId, type = "private" }
            };

            var json = JsonSerializer.Serialize(payload);
            return JsonSerializer.Deserialize<Message>(json, JsonOptions)
                   ?? throw new InvalidOperationException("Failed to deserialize message");
        }

        private static Update DeserializeUpdate(object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            return JsonSerializer.Deserialize<Update>(json, JsonOptions)
                   ?? throw new InvalidOperationException("Failed to deserialize update");
        }
    }
}
