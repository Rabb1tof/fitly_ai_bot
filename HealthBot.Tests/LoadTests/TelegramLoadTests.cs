using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HealthBot.Core.Entities;
using HealthBot.Infrastructure.Data;
using HealthBot.Infrastructure.Services;
using HealthBot.Infrastructure.Telegram;
using HealthBot.Infrastructure.Telegram.Commands;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Shared.Options;
using HealthBot.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace HealthBot.Tests.LoadTests;

/// <summary>
/// Нагрузочные тесты для проверки поведения системы под нагрузкой.
/// Симулируют 1000+ одновременных пользователей.
/// </summary>
public class TelegramLoadTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly HealthBotDbContext _dbContext;

    public TelegramLoadTests(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();

        // In-memory database для тестов
        services.AddDbContextPool<HealthBotDbContext>(options =>
            options.UseInMemoryDatabase($"LoadTest_{Guid.NewGuid()}"),
            poolSize: 256);

        // Конфигурация
        services.Configure<RedisOptions>(options =>
        {
            options.DefaultTtlMinutes = 5;
            options.MessageRateLimitPerMinute = 100;
            options.CallbackRateLimitPerMinute = 200;
        });

        services.Configure<TelegramOptions>(options =>
        {
            options.BotToken = "test_token";
        });

        // Сервисы
        services.AddSingleton<IRedisCacheService, InMemoryRedisCacheService>();
        services.AddSingleton<IConversationContextStore, InMemoryConversationContextStore>();
        services.AddScoped<UserService>();
        services.AddScoped<ReminderService>();
        services.AddSingleton<ILogger<TelegramUpdateHandler>>(NullLogger<TelegramUpdateHandler>.Instance);
        services.AddSingleton<ILogger<CommandDispatcher>>(NullLogger<CommandDispatcher>.Instance);

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<HealthBotDbContext>();
    }

    [Trait("Category", "Load")]
    [Fact]
    public async Task LoadTest_1000_ConcurrentUsers_ShouldHandleWithoutMemoryLeak()
    {
        // Arrange
        const int userCount = 1000;
        const int operationsPerUser = 10;

        var stopwatch = Stopwatch.StartNew();
        var initialMemory = GC.GetTotalMemory(forceFullCollection: true);

        _output.WriteLine($"Начальная память: {initialMemory / 1024 / 1024} MB");
        _output.WriteLine($"Симуляция {userCount} пользователей с {operationsPerUser} операциями каждый");

        // Act - симулируем регистрацию пользователей и создание напоминаний
        var tasks = new List<Task>();
        for (int userId = 1; userId <= userCount; userId++)
        {
            var chatId = userId;
            var task = Task.Run(async () =>
            {
                for (int op = 1; op <= operationsPerUser; op++)
                {
                    await using var scope = _serviceProvider.CreateAsyncScope();
                    var userService = scope.ServiceProvider.GetRequiredService<UserService>();
                    var dbContext = scope.ServiceProvider.GetRequiredService<HealthBotDbContext>();
                    
                    await userService.RegisterUserAsync(chatId, $"user{chatId}");
                    
                    // Проверяем, что ChangeTracker пуст
                    var trackedCount = dbContext.ChangeTracker.Entries().Count();
                    Assert.True(trackedCount == 0, $"ChangeTracker не пуст: {trackedCount} сущностей");
                }
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Принудительная сборка мусора для точного измерения
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(forceFullCollection: false);
        var memoryGrowth = finalMemory - initialMemory;

        // Assert
        _output.WriteLine($"Время выполнения: {stopwatch.Elapsed.TotalSeconds:F2} сек");
        _output.WriteLine($"Конечная память: {finalMemory / 1024 / 1024} MB");
        _output.WriteLine($"Рост памяти: {memoryGrowth / 1024 / 1024} MB");
        _output.WriteLine($"Среднее время на операцию: {stopwatch.ElapsedMilliseconds / (userCount * operationsPerUser):F2} мс");

        // Проверяем, что рост памяти разумный (не более 100 MB на 10000 операций)
        Assert.True(memoryGrowth < 100 * 1024 * 1024, 
            $"Рост памяти слишком большой: {memoryGrowth / 1024 / 1024} MB");
    }

    [Trait("Category", "Load")]
    [Fact]
    public async Task LoadTest_DbContextPool_ShouldReuseContexts()
    {
        // Arrange
        const int iterations = 1000;
        var contextIds = new HashSet<int>();

        // Act
        for (int i = 0; i < iterations; i++)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HealthBotDbContext>();
            contextIds.Add(dbContext.GetHashCode());
        }

        // Assert
        _output.WriteLine($"Уникальных контекстов создано: {contextIds.Count} из {iterations} итераций");
        
        // Пул должен переиспользовать контексты, поэтому уникальных должно быть значительно меньше
        Assert.True(contextIds.Count < iterations / 2, 
            "DbContextPool не переиспользует контексты эффективно");
    }

    [Trait("Category", "Load")]
    [Fact]
    public async Task LoadTest_ConversationContextStore_ShouldCleanupExpiredSessions()
    {
        // Arrange
        var store = _serviceProvider.GetRequiredService<IConversationContextStore>() as InMemoryConversationContextStore;
        Assert.NotNull(store);

        const int sessionCount = 200;

        for (long chatId = 1; chatId <= sessionCount; chatId++)
        {
            var session = await store!.GetSessionAsync(chatId);
            session.Flow = ConversationFlow.Template;
        }

        Assert.Equal(sessionCount, GetSessionCount(store!));

        // Имитация истечения TTL и запуска очистки через reflection
        InvokeCleanup(store!, DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal(0, GetSessionCount(store!));
    }

    [Trait("Category", "Load")]
    [Fact]
    public async Task LoadTest_UserService_ShouldNotLeakEntities()
    {
        // Arrange
        const int userCount = 500;
        var initialMemory = GC.GetTotalMemory(forceFullCollection: true);

        // Act
        for (int i = 1; i <= userCount; i++)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<HealthBotDbContext>();

            await userService.RegisterUserAsync(i, $"user{i}");

            // Проверяем, что ChangeTracker не накапливает сущности
            var trackedCount = dbContext.ChangeTracker.Entries().Count();
            Assert.True(trackedCount == 0, 
                $"ChangeTracker содержит {trackedCount} отслеживаемых сущностей после обработки");
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(forceFullCollection: false);
        var memoryGrowth = finalMemory - initialMemory;

        // Assert
        _output.WriteLine($"Обработано {userCount} пользователей");
        _output.WriteLine($"Рост памяти: {memoryGrowth / 1024 / 1024} MB");

        Assert.True(memoryGrowth < 50 * 1024 * 1024, 
            $"Утечка памяти в UserService: {memoryGrowth / 1024 / 1024} MB");
    }

    [Trait("Category", "Load")]
    [Fact]
    public async Task LoadTest_ReminderService_ShouldNotLeakEntities()
    {
        // Arrange
        const int reminderCount = 500;
        var initialMemory = GC.GetTotalMemory(forceFullCollection: true);

        // Создаём тестового пользователя
        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            await userService.RegisterUserAsync(1, "testuser");
        }

        var userId = (await _dbContext.Users.FirstAsync()).Id;

        // Act
        for (int i = 1; i <= reminderCount; i++)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var reminderService = scope.ServiceProvider.GetRequiredService<ReminderService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<HealthBotDbContext>();

            await reminderService.ScheduleReminderAsync(
                userId,
                $"Reminder {i}",
                DateTime.UtcNow.AddMinutes(i),
                repeatIntervalMinutes: null);

            // Проверяем отсоединение
            var trackedCount = dbContext.ChangeTracker.Entries().Count();
            Assert.True(trackedCount == 0, 
                $"ChangeTracker содержит {trackedCount} отслеживаемых сущностей");
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(forceFullCollection: false);
        var memoryGrowth = finalMemory - initialMemory;

        // Assert
        _output.WriteLine($"Создано {reminderCount} напоминаний");
        _output.WriteLine($"Рост памяти: {memoryGrowth / 1024 / 1024} MB");

        Assert.True(memoryGrowth < 50 * 1024 * 1024, 
            $"Утечка памяти в ReminderService: {memoryGrowth / 1024 / 1024} MB");
    }

    private static void InvokeCleanup(InMemoryConversationContextStore store, DateTimeOffset now)
    {
        var method = typeof(InMemoryConversationContextStore)
            .GetMethod("CleanupExpiredSessions", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(DateTimeOffset) }, null);
        Assert.NotNull(method);
        method!.Invoke(store, new object[] { now });
    }

    private static int GetSessionCount(InMemoryConversationContextStore store)
    {
        var sessionsField = typeof(InMemoryConversationContextStore)
            .GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic);
        var sessionsObj = sessionsField!.GetValue(store);
        var countProp = sessionsObj?.GetType().GetProperty("Count");
        return (int)(countProp?.GetValue(sessionsObj) ?? 0);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
        _serviceProvider?.Dispose();
    }
}
