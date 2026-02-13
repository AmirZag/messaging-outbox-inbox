namespace Messaging.OutboxInbox.AspNetCore.Options;

public sealed class MessagePublisherOptions
{
    public const string Section = "MessagePublisher";

    public string ExchangeName { get; set; } = "messaging.events";
    public string RoutingKey { get; set; } = "events";
}