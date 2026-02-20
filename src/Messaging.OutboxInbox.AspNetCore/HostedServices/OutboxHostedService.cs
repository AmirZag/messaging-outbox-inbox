using Messaging.OutboxInbox.AspNetCore.Extensions;
using Messaging.OutboxInbox.AspNetCore.MessageBroker;
using Messaging.OutboxInbox.AspNetCore.Queues;
using Messaging.OutboxInbox.Entities;
using Messaging.OutboxInbox.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Messaging.OutboxInbox.AspNetCore.HostedServices;

internal sealed class OutboxHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOutboxMessageQueue _outboxQueue;
    private readonly ILogger<OutboxHostedService> _logger;
    private const string ServiceName = "OutboxHostedService";

    public OutboxHostedService(
        IServiceProvider serviceProvider,
        IOutboxMessageQueue outboxQueue,
        ILogger<OutboxHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _outboxQueue = outboxQueue;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.HostedServiceStarted(ServiceName);
        await LoadUnprocessedMessagesAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = await _outboxQueue.DequeueAsync(stoppingToken);

                if (message is null)
                {
                    _logger.LogWarning("Dequeued null outbox message - skipping");
                    continue;
                }

                await ProcessMessageAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.HostedServiceError(ServiceName, ex);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.HostedServiceStopped(ServiceName);
    }

    private async Task LoadUnprocessedMessagesAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var outboxService = scope.ServiceProvider.GetRequiredService<IOutboxMessagesService>();

        var unprocessed = await outboxService.GetUnprocessedListAsync(cancellationToken);
        var list = unprocessed.ToList();

        foreach (var message in list)
            _outboxQueue.Enqueue(message);

        if (list.Count > 0)
            _logger.OutboxUnprocessedMessagesLoaded(list.Count);
        else
            _logger.LogInformation("No unprocessed outbox messages found on startup");
    }

    private async Task ProcessMessageAsync(OutboxRecord message, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var outboxService = scope.ServiceProvider.GetRequiredService<IOutboxMessagesService>();
        var publisher = scope.ServiceProvider.GetRequiredService<RabbitMqPublisher>();

        try
        {
            _logger.OutboxMessageProcessing(message.Id, message.Type);

            if (await outboxService.IsProcessedAsync(message.Id, cancellationToken))
            {
                _logger.OutboxMessageAlreadyProcessed(message.Id, message.Type);
                return;
            }

            await publisher.PublishAsync(message, cancellationToken);
            await outboxService.MarkAsProcessedAsync(message.Id, cancellationToken);

            stopwatch.Stop();
            _logger.OutboxMessageProcessed(message.Id, message.Type, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorMessage = $"{ex.GetType().Name}: {ex.Message}";

            _logger.OutboxMessageFailed(message.Id, message.Type, errorMessage, ex);

            try
            {
                await outboxService.MarkAsFailedAsync(message.Id, errorMessage, cancellationToken);
            }
            catch (Exception markFailedEx)
            {
                _logger.LogError(markFailedEx,
                    "Failed to mark outbox message as failed - MessageId: {MessageId}", message.Id);
            }

        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping {ServiceName}", ServiceName);
        await base.StopAsync(cancellationToken);
    }
}