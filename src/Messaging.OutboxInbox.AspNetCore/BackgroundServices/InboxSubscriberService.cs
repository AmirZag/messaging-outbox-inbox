// src/Messaging.OutboxInbox.AspNetCore/BackgroundServices/InboxSubscriberService.cs
using Messaging.OutboxInbox.AspNetCore.Subscribers;
using Microsoft.Extensions.Hosting;

namespace Messaging.OutboxInbox.AspNetCore.BackgroundServices;

internal sealed class InboxSubscriberService : IHostedService
{
    private readonly RabbitMqSubscriber _subscriber;

    public InboxSubscriberService(RabbitMqSubscriber subscriber)
    {
        _subscriber = subscriber;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _subscriber.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}