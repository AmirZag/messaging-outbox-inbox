// src/Messaging.OutboxInbox.AspNetCore/BackgroundServices/InboxProcessorService.cs
using System.Text.Json;
using MediatR;
using Messaging.OutboxInbox.AspNetCore.Options;
using Messaging.OutboxInbox.AspNetCore.Retry;
using Messaging.OutboxInbox.AspNetCore.Signals;
using Messaging.OutboxInbox.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Messaging.OutboxInbox.AspNetCore.BackgroundServices;

internal sealed class InboxProcessorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IInboxSignal _signal;
    private readonly IRetryTracker _retryTracker;
    private readonly ILogger<InboxProcessorService> _logger;
    private readonly InboxProcessorOptions _options;

    public InboxProcessorService(
        IServiceProvider serviceProvider,
        IInboxSignal signal,
        IRetryTracker retryTracker,
        IOptions<InboxProcessorOptions> options,
        ILogger<InboxProcessorService> logger)
    {
        _serviceProvider = serviceProvider;
        _signal = signal;
        _retryTracker = retryTracker;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Inbox Processor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for signal OR timeout
                await _signal.WaitAsync(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);

                // Poll database for unprocessed messages
                var messages = await PollDatabaseAsync(stoppingToken);

                // Process each message
                foreach (var message in messages)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    await ProcessMessageAsync(message, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in inbox processor");
                await Task.Delay(TimeSpan.FromSeconds(_options.ErrorRetryDelaySeconds), stoppingToken);
            }
        }

        _logger.LogInformation("Inbox Processor stopped");
    }

    private async Task<IEnumerable<Messaging.OutboxInbox.Entities.InboxRecord>> PollDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var inboxService = scope.ServiceProvider.GetRequiredService<IInboxMessagesService>();

            var messages = await inboxService.GetUnprocessedListAsync(_options.BatchSize, cancellationToken);

            if (messages.Any())
            {
                _logger.LogDebug("Polled {Count} unprocessed inbox messages", messages.Count());
            }

            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling inbox database");
            return Enumerable.Empty<Messaging.OutboxInbox.Entities.InboxRecord>();
        }
    }

    private async Task ProcessMessageAsync(Messaging.OutboxInbox.Entities.InboxRecord message, CancellationToken cancellationToken)
    {
        // Check if we should retry this message
        if (message.Error is not null && !_retryTracker.ShouldRetry(message.Id, _options.MaxRetryAttempts))
        {
            _logger.LogWarning("Message {MessageId} exceeded max retry attempts ({MaxAttempts}), skipping",
                message.Id, _options.MaxRetryAttempts);
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var inboxService = scope.ServiceProvider.GetRequiredService<IInboxMessagesService>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        try
        {
            // Clear error if retrying
            if (message.Error is not null)
            {
                await inboxService.ClearErrorAsync(message.Id, cancellationToken);
                _logger.LogInformation("Retrying message {MessageId}", message.Id);
            }

            var messageType = Type.GetType(message.Type);
            if (messageType is null)
                throw new InvalidOperationException($"Type not found: {message.Type}");

            var deserializedMessage = JsonSerializer.Deserialize(message.Content, messageType);
            if (deserializedMessage is null)
                throw new InvalidOperationException($"Failed to deserialize: {message.Id}");

            await mediator.Send(deserializedMessage, cancellationToken);
            await inboxService.MarkAsProcessedAsync(message.Id, cancellationToken);
            _retryTracker.Reset(message.Id);

            _logger.LogInformation("Processed inbox message {MessageId}", message.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process inbox message {MessageId}", message.Id);

            _retryTracker.RecordAttempt(message.Id);
            await inboxService.MarkAsFailedAsync(message.Id, ex.Message, cancellationToken);
        }
    }
}