using System;
using System.Threading;
using System.Threading.Tasks;
using HealthBot.Shared.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace HealthBot.Infrastructure.Services;

public class TelegramPollingService : BackgroundService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUpdateHandler _updateHandler;
    private readonly ILogger<TelegramPollingService> _logger;
    private readonly TimeSpan _restartInterval;

    public TelegramPollingService(
        ITelegramBotClient botClient,
        IUpdateHandler updateHandler,
        ILogger<TelegramPollingService> logger,
        IOptions<TelegramOptions> telegramOptions)
    {
        _botClient = botClient;
        _updateHandler = updateHandler;
        _logger = logger;
        _restartInterval = telegramOptions.Value.GetPollingRestartInterval();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>(),
            DropPendingUpdates = true
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            CancellationTokenSource? linkedCts = null;
            try
            {
                if (_restartInterval > TimeSpan.Zero)
                {
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    linkedCts.CancelAfter(_restartInterval);
                }

                var effectiveToken = linkedCts?.Token ?? stoppingToken;
                await _botClient.ReceiveAsync(
                    updateHandler: _updateHandler,
                    receiverOptions: receiverOptions,
                    cancellationToken: effectiveToken);
            }
            catch (OperationCanceledException) when (linkedCts is not null && !stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Restarting polling after {Interval}", _restartInterval);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Polling loop crashed. Restarting in 5 seconds");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                linkedCts?.Dispose();
            }
        }
    }
}
