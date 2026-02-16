using Messaging.OutboxInbox.AspNetCore.MessageBroker;
using Messaging.OutboxInbox.AspNetCore.Queues;
using Messaging.OutboxInbox.Entities;
using Messaging.OutboxInbox.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Messaging.OutboxInbox.AspNetCore.HostedServices;

internal sealed class OutboxHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOutboxMessageQueue _outboxQueue;
    private readonly ILogger<OutboxHostedService> _logger;

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
        await LoadUnprocessedMessagesAsync(cancellationToken);

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Hosted Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                OutboxRecord? message = await _outboxQueue.DequeueAsync(stoppingToken);

                if (message is null) continue;

                await ProcessMessageAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in outbox processor");
            }
        }

        _logger.LogInformation("Outbox Hosted Service stopped");
    }

    private async Task LoadUnprocessedMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
            IOutboxMessagesService outboxService = scope.ServiceProvider.GetRequiredService<IOutboxMessagesService>();

            IEnumerable<OutboxRecord> unprocessedMessages = await outboxService.GetUnprocessedListAsync(cancellationToken);

            foreach (OutboxRecord message in unprocessedMessages)
            {
                _outboxQueue.Enqueue(message);
            }

            if (unprocessedMessages.Any())
            {
                _logger.LogInformation("Loaded {Count} unprocessed outbox messages", unprocessedMessages.Count());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading unprocessed outbox messages");
        }
    }

    private async Task ProcessMessageAsync(OutboxRecord message, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();

        IOutboxMessagesService outboxService = scope.ServiceProvider.GetRequiredService<IOutboxMessagesService>();
        
        RabbitMqPublisher rabbitMqPublisher = scope.ServiceProvider.GetRequiredService<RabbitMqPublisher>();

        try
        {

            bool isProcessed = await outboxService.IsProcessedAsync(message.Id, cancellationToken);
            if (isProcessed)
            {
                _logger.LogInformation("Outbox message {MessageId} already processed, skipping", message.Id);
                return;
            }

            await rabbitMqPublisher.PublishAsync(message, cancellationToken);

            await outboxService.MarkAsProcessedAsync(message.Id, cancellationToken);

            _logger.LogInformation("Processed outbox message {MessageId}", message.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process outbox message {MessageId}", message.Id);

            await outboxService.MarkAsFailedAsync(message.Id, ex.Message, cancellationToken);
        }
    }
}