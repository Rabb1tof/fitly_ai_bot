using System;
using System.Net.Http;
using HealthBot.Infrastructure.Data;
using HealthBot.Infrastructure.Services;
using HealthBot.Shared.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));

builder.Services.AddDbContext<HealthBotDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"),
        npgsqlOptions => npgsqlOptions.MigrationsAssembly(typeof(HealthBotDbContext).Assembly.GetName().Name)));

builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ReminderService>();
builder.Services.AddHostedService<ReminderWorker>();
builder.Services.AddHttpClient("telegram");
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
