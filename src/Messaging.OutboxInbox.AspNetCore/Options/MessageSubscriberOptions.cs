namespace Messaging.OutboxInbox.AspNetCore.Options;

public sealed class MessageSubscriberOptions
{
    public const string Section = "MessageSubscriber";

    public string ExchangeName { get; set; } = "messaging.events";
    public string QueueName { get; set; } = "inbox.queue";
    public string RoutingKey { get; set; } = "events";
    public ushort PrefetchCount { get; set; } = 10;
}