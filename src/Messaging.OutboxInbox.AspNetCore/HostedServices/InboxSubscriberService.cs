using Messaging.OutboxInbox.AspNetCore.MessageBroker;
using Microsoft.Extensions.Hosting;

namespace Messaging.OutboxInbox.AspNetCore.HostedServices;

internal sealed class InboxSubscriberService : IHostedService
{
    private readonly RabbitMqSubscriber _subscriber;

    public InboxSubscriberService(RabbitMqSubscriber subscriber)
    {
        _subscriber = subscriber;
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => _subscriber.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}