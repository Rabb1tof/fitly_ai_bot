using System;
using System.Net.Http;
using HealthBot.Infrastructure.Data;
using HealthBot.Infrastructure.Services;
using HealthBot.Infrastructure.Telegram;
using HealthBot.Infrastructure.Telegram.Commands;
using HealthBot.Infrastructure.Telegram.Commands.Abstractions;
using HealthBot.Infrastructure.Telegram.Commands.Callback;
using HealthBot.Infrastructure.Telegram.Commands.Message;
using HealthBot.Shared.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection(RedisOptions.SectionName));

builder.Services.AddDbContextPool<HealthBotDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"),
        npgsqlOptions => npgsqlOptions.MigrationsAssembly(typeof(HealthBotDbContext).Assembly.GetName().Name)),
    poolSize: 128);

builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ReminderService>();
builder.Services.AddHostedService<ReminderWorker>();
builder.Services.AddHttpClient("telegram");

var redisConnectionString = builder.Configuration.GetSection(RedisOptions.SectionName).GetValue<string>(nameof(RedisOptions.ConnectionString));
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
    builder.Services.AddSingleton<IRedisCacheService, RedisCacheService>();
    builder.Services.AddSingleton<IConversationContextStore, RedisConversationContextStore>();
}
else
{
    builder.Services.AddSingleton<IRedisCacheService, NoOpRedisCacheService>();
    builder.Services.AddSingleton<IConversationContextStore, InMemoryConversationContextStore>();
}

builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
    if (string.IsNullOrWhiteSpace(options.BotToken))
    {
        throw new InvalidOperationException("Telegram bot token is not configured.");
    }

    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("telegram");
    var botOptions = new TelegramBotClientOptions(options.BotToken);
    return new TelegramBotClient(botOptions, httpClient);
});
builder.Services.AddSingleton<ITelegramCommandHandler, StartCommandHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, CancelCommandHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, ManualInputMessageHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, UnknownMessageHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, MenuCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, MainRemindersCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, MainNutritionCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, MainSettingsCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, RemindersListCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, RemindersTemplatesCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, CustomNewCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, CustomDelayCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, CustomRepeatCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, TemplateSelectCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, TemplateDelayCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, TemplateRepeatCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, ReminderDisableCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, SettingsTimezoneCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, SettingsTimezoneSelectCallbackHandler>();
builder.Services.AddSingleton<ITelegramCommandHandler, SettingsTimezoneManualCallbackHandler>();
builder.Services.AddSingleton<CommandDispatcher>();
builder.Services.AddSingleton<IUpdateHandler, TelegramUpdateHandler>();
builder.Services.AddHostedService<TelegramPollingService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<HealthBotDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapGet("/", () => "HealthBot is running");

app.Run();
