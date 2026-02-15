using MediatR;
using Messaging.OutboxInbox.AspNetCore.Queues;
using Messaging.OutboxInbox.Entities;
using Messaging.OutboxInbox.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Messaging.OutboxInbox.AspNetCore.HostedServices;

internal sealed class InboxHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IInboxMessageQueue _inboxQueue;
    private readonly ILogger<InboxHostedService> _logger;

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
        await LoadUnprocessedMessagesAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Inbox Hosted Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                InboxRecord? message = await _inboxQueue.DequeueAsync(stoppingToken);

                if (message is null) continue;

                await ProcessMessageAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in inbox processor");
            }
        }

        _logger.LogInformation("Inbox Hosted Service stopped");
    }

    private async Task LoadUnprocessedMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();

            IInboxMessagesService inboxService = scope.ServiceProvider.GetRequiredService<IInboxMessagesService>();

            IEnumerable<InboxRecord> unprocessedMessages = await inboxService.GetUnprocessedListAsync(cancellationToken);

            foreach (InboxRecord message in unprocessedMessages)
            {
                _inboxQueue.Enqueue(message);
            }

            if (unprocessedMessages.Any())
            {
                _logger.LogInformation("Loaded {Count} unprocessed inbox messages", unprocessedMessages.Count());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading unprocessed inbox messages");
        }
    }

    private async Task ProcessMessageAsync(InboxRecord message, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();

        IInboxMessagesService inboxService = scope.ServiceProvider.GetRequiredService<IInboxMessagesService>();

        IMediator mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        try
        {
            Type? messageType = Type.GetType(message.Type);
            if (messageType is null)
                throw new InvalidOperationException($"Type not found: {message.Type}");

            object? deserializedMessage = JsonSerializer.Deserialize(message.Content, messageType);
            if (deserializedMessage is null)
                throw new InvalidOperationException($"Failed to deserialize message {message.Id}");

            await mediator.Send(deserializedMessage, cancellationToken);
            await inboxService.MarkAsProcessedAsync(message.Id, cancellationToken);

            _logger.LogInformation("Processed inbox message {MessageId}", message.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process inbox message {MessageId}", message.Id);
            await inboxService.MarkAsFailedAsync(message.Id, ex.Message, cancellationToken);
        }
    }
}