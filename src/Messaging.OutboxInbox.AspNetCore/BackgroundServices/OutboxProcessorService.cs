// src/Messaging.OutboxInbox.AspNetCore/BackgroundServices/OutboxProcessorService.cs
using Messaging.OutboxInbox.AspNetCore.Options;
using Messaging.OutboxInbox.AspNetCore.Publishers;
using Messaging.OutboxInbox.AspNetCore.Retry;
using Messaging.OutboxInbox.AspNetCore.Signals;
using Messaging.OutboxInbox.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Messaging.OutboxInbox.AspNetCore.BackgroundServices;

internal sealed class OutboxProcessorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOutboxSignal _signal;
    private readonly IRetryTracker _retryTracker;
    private readonly ILogger<OutboxProcessorService> _logger;
    private readonly OutboxProcessorOptions _options;

    public OutboxProcessorService(
        IServiceProvider serviceProvider,
        IOutboxSignal signal,
        IRetryTracker retryTracker,
        IOptions<OutboxProcessorOptions> options,
        ILogger<OutboxProcessorService> logger)
    {
        _serviceProvider = serviceProvider;
        _signal = signal;
        _retryTracker = retryTracker;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Processor started");

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
                _logger.LogError(ex, "Error in outbox processor");
                await Task.Delay(TimeSpan.FromSeconds(_options.ErrorRetryDelaySeconds), stoppingToken);
            }
        }

        _logger.LogInformation("Outbox Processor stopped");
    }

    private async Task<IEnumerable<Messaging.OutboxInbox.Entities.OutboxRecord>> PollDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var outboxService = scope.ServiceProvider.GetRequiredService<IOutboxMessagesService>();

            var messages = await outboxService.GetUnprocessedListAsync(_options.BatchSize, cancellationToken);

            if (messages.Any())
            {
                _logger.LogDebug("Polled {Count} unprocessed outbox messages", messages.Count());
            }

            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling outbox database");
            return Enumerable.Empty<Messaging.OutboxInbox.Entities.OutboxRecord>();
        }
    }

    private async Task ProcessMessageAsync(Messaging.OutboxInbox.Entities.OutboxRecord message, CancellationToken cancellationToken)
    {
        // Check if we should retry this message
        if (message.Error is not null && !_retryTracker.ShouldRetry(message.Id, _options.MaxRetryAttempts))
        {
            _logger.LogWarning("Message {MessageId} exceeded max retry attempts ({MaxAttempts}), skipping",
                message.Id, _options.MaxRetryAttempts);
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var outboxService = scope.ServiceProvider.GetRequiredService<IOutboxMessagesService>();
        var rabbitMqPublisher = scope.ServiceProvider.GetRequiredService<RabbitMqPublisher>();

        try
        {
            // Clear error if retrying
            if (message.Error is not null)
            {
                await outboxService.ClearErrorAsync(message.Id, cancellationToken);
                _logger.LogInformation("Retrying message {MessageId}", message.Id);
            }

            await rabbitMqPublisher.PublishToRabbitMqAsync(
                message.Id,
                message.Type,
                message.Content,
                message.OccurredAt,
                cancellationToken);

            await outboxService.MarkAsProcessedAsync(message.Id, cancellationToken);
            _retryTracker.Reset(message.Id);

            _logger.LogInformation("Processed outbox message {MessageId}", message.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process outbox message {MessageId}", message.Id);

            _retryTracker.RecordAttempt(message.Id);
            await outboxService.MarkAsFailedAsync(message.Id, ex.Message, cancellationToken);
        }
    }
}