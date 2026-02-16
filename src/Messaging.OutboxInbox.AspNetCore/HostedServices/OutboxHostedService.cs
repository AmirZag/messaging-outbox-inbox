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

    public OutboxHostedService(IServiceProvider serviceProvider,
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

        try
        {
            await LoadUnprocessedMessagesAsync(cancellationToken);
            await base.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.HostedServiceError(ServiceName, ex);
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Entering {Method}", nameof(ExecuteAsync));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                OutboxRecord? message = await _outboxQueue.DequeueAsync(stoppingToken);

                if (message is null)
                {
                    _logger.LogWarning("Dequeued null message from outbox queue - this should not happen");
                    continue;
                }

                await ProcessMessageAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug("Outbox processing cancelled - shutting down gracefully");
                break;
            }
            catch (Exception ex)
            {
                _logger.HostedServiceError(ServiceName, ex);

                // Continue processing other messages
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.HostedServiceStopped(ServiceName);
        _logger.LogDebug("Exiting {Method}", nameof(ExecuteAsync));
    }

    private async Task LoadUnprocessedMessagesAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Entering {Method}", nameof(LoadUnprocessedMessagesAsync));

        try
        {
            await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
            IOutboxMessagesService outboxService = scope.ServiceProvider.GetRequiredService<IOutboxMessagesService>();

            IEnumerable<OutboxRecord> unprocessedMessages = await outboxService.GetUnprocessedListAsync(cancellationToken);
            var messagesList = unprocessedMessages.ToList();

            if (!messagesList.Any())
            {
                _logger.LogInformation("No unprocessed outbox messages found on startup");
                return;
            }

            foreach (OutboxRecord message in messagesList)
            {
                _outboxQueue.Enqueue(message);
                _logger.OutboxMessageEnqueued(message.Id, message.Type);
            }

            _logger.OutboxUnprocessedMessagesLoaded(messagesList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "CRITICAL: Failed to load unprocessed outbox messages on startup - messages may be lost");
            throw; // Re-throw to prevent service from starting in bad state
        }
        finally
        {
            _logger.LogDebug("Exiting {Method}", nameof(LoadUnprocessedMessagesAsync));
        }
    }

    private async Task ProcessMessageAsync(OutboxRecord message, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Entering {Method} - MessageId: {MessageId}", nameof(ProcessMessageAsync), message.Id);
        var stopwatch = Stopwatch.StartNew();

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        IOutboxMessagesService outboxService = scope.ServiceProvider.GetRequiredService<IOutboxMessagesService>();
        RabbitMqPublisher rabbitMqPublisher = scope.ServiceProvider.GetRequiredService<RabbitMqPublisher>();

        try
        {
            _logger.OutboxMessageProcessing(message.Id, message.Type);

            // Idempotency check
            var isProcessed = await outboxService.IsProcessedAsync(message.Id, cancellationToken);
            if (isProcessed)
            {
                _logger.OutboxMessageAlreadyProcessed(message.Id, message.Type);
                return;
            }

            // Publish to RabbitMQ
            await rabbitMqPublisher.PublishAsync(message, cancellationToken);

            // Mark as processed
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
                    "CRITICAL: Failed to mark outbox message as failed - MessageId: {MessageId}, OriginalError: {OriginalError}",
                    message.Id, errorMessage);
            }

            // Re-throw to allow retry mechanism if configured
            throw;
        }
        finally
        {
            _logger.LogDebug("Exiting {Method} - MessageId: {MessageId}, Duration: {DurationMs}ms",
                nameof(ProcessMessageAsync), message.Id, stopwatch.ElapsedMilliseconds);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping {ServiceName} - waiting for in-flight messages", ServiceName);
        await base.StopAsync(cancellationToken);
    }
}