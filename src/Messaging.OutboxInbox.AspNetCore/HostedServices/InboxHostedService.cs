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
        await LoadUnprocessedMessagesAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = await _inboxQueue.DequeueAsync(stoppingToken);

                if (message is null)
                {
                    _logger.LogWarning("Dequeued null inbox message - skipping");
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
        var inboxService = scope.ServiceProvider.GetRequiredService<IInboxMessagesService>();

        var unprocessed = await inboxService.GetUnprocessedListAsync(cancellationToken);
        var list = unprocessed.ToList();

        foreach (var message in list)
            _inboxQueue.Enqueue(message);

        if (list.Count > 0)
            _logger.InboxUnprocessedMessagesLoaded(list.Count);
        else
            _logger.LogInformation("No unprocessed inbox messages found on startup");
    }

    private async Task ProcessMessageAsync(InboxRecord message, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var inboxService = scope.ServiceProvider.GetRequiredService<IInboxMessagesService>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        try
        {
            _logger.InboxMessageProcessing(message.Id, message.Type);

            if (await inboxService.IsProcessedAsync(message.Id, cancellationToken))
            {
                _logger.InboxMessageAlreadyProcessed(message.Id, message.Type);
                return;
            }

            var messageType = Type.GetType(message.Type)
                ?? throw new InvalidOperationException($"Type not found: {message.Type}");

            var deserialized = JsonSerializer.Deserialize(message.Content, messageType)
                ?? throw new InvalidOperationException("Failed to deserialize message content");

            await mediator.Send(deserialized, cancellationToken);
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
                    "Failed to mark inbox message as failed - MessageId: {MessageId}", message.Id);
            }

        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping {ServiceName}", ServiceName);
        await base.StopAsync(cancellationToken);
    }
}