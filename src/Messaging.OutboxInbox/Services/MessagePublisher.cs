using System.Text.Json;
using Messaging.OutboxInbox.Abstractions;
using Messaging.OutboxInbox.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messaging.OutboxInbox.Services;

public sealed class MessagePublisher : IMessagePublisher
{
    private readonly IServiceProvider _serviceProvider;

    public MessagePublisher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : IMessage
    {
        // Resolve from current scope - ensures we get the same instance as the caller
        var context = _serviceProvider.GetRequiredService<DbContext>();

        var messageType = typeof(TMessage).AssemblyQualifiedName
            ?? throw new InvalidOperationException($"Cannot determine type name for {typeof(TMessage).Name}");

        var content = JsonSerializer.Serialize(message);

        var outboxRecord = new OutboxRecord
        {
            Id = Guid.NewGuid(),
            Type = messageType,
            Content = content,
            OccurredAt = DateTime.UtcNow
        };

        context.Set<OutboxRecord>().Add(outboxRecord);

        return Task.CompletedTask;
    }
}