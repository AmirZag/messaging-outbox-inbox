using MediatR;
using Messaging.OutboxInbox.AspNetCore.Extensions;
using Messaging.OutboxInbox.AspNetCore.Queues;
using Messaging.OutboxInbox.Entities;
using Messaging.OutboxInbox.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Messaging.OutboxInbox.AspNetCore.HostedServices;

internal sealed class InboxHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IInboxMessageQueue _inboxQueue;
    private readonly ILogger<InboxHostedService> _logger;
    private const string ServiceName = "InboxHostedService";

    public InboxHostedService(
        IServiceProvider serviceProvider,
        IInboxMessageQueue inboxQueue,
        ILogger<InboxHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _inboxQueue = inboxQueue;
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
                InboxRecord? message = await _inboxQueue.DequeueAsync(stoppingToken);

                if (message is null)
                {
                    _logger.LogWarning("Dequeued null message from inbox queue - this should not happen");
                    continue;
                }

                await ProcessMessageAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug("Inbox processing cancelled - shutting down gracefully");
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
            IInboxMessagesService inboxService = scope.ServiceProvider.GetRequiredService<IInboxMessagesService>();

            IEnumerable<InboxRecord> unprocessedMessages = await inboxService.GetUnprocessedListAsync(cancellationToken);
            var messagesList = unprocessedMessages.ToList();

            if (!messagesList.Any())
            {
                _logger.LogInformation("No unprocessed inbox messages found on startup");
                return;
            }

            foreach (InboxRecord message in messagesList)
            {
                _inboxQueue.Enqueue(message);
                _logger.InboxMessageEnqueued(message.Id, message.Type);
            }

            _logger.InboxUnprocessedMessagesLoaded(messagesList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "CRITICAL: Failed to load unprocessed inbox messages on startup - messages may be lost");
            throw; // Re-throw to prevent service from starting in bad state
        }
        finally
        {
            _logger.LogDebug("Exiting {Method}", nameof(LoadUnprocessedMessagesAsync));
        }
    }

    private async Task ProcessMessageAsync(InboxRecord message, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Entering {Method} - MessageId: {MessageId}", nameof(ProcessMessageAsync), message.Id);
        var stopwatch = Stopwatch.StartNew();

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        IInboxMessagesService inboxService = scope.ServiceProvider.GetRequiredService<IInboxMessagesService>();
        IMediator mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        try
        {
            _logger.InboxMessageProcessing(message.Id, message.Type);

            // Idempotency check
            var isProcessed = await inboxService.IsProcessedAsync(message.Id, cancellationToken);
            if (isProcessed)
            {
                _logger.InboxMessageAlreadyProcessed(message.Id, message.Type);
                return;
            }

            // Deserialize message
            Type? messageType = Type.GetType(message.Type);
            if (messageType is null)
            {
                throw new InvalidOperationException($"Type not found: {message.Type}");
            }

            object? deserializedMessage = JsonSerializer.Deserialize(message.Content, messageType);
            if (deserializedMessage is null)
            {
                throw new InvalidOperationException($"Failed to deserialize message - content may be malformed");
            }

            // Send to handler via MediatR
            await mediator.Send(deserializedMessage, cancellationToken);

            // Mark as processed
            await inboxService.MarkAsProcessedAsync(message.Id, cancellationToken);

            stopwatch.Stop();
            _logger.InboxMessageProcessed(message.Id, message.Type, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorMessage = $"{ex.GetType().Name}: {ex.Message}";

            _logger.InboxMessageFailed(message.Id, message.Type, errorMessage, ex);

            try
            {
                await inboxService.MarkAsFailedAsync(message.Id, errorMessage, cancellationToken);
            }
            catch (Exception markFailedEx)
            {
                _logger.LogError(markFailedEx,
                    "CRITICAL: Failed to mark inbox message as failed - MessageId: {MessageId}, OriginalError: {OriginalError}",
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