namespace Messaging.OutboxInbox.AspNetCore.Options;

public sealed class RabbitMqOptions
{
    public const string Section = "RabbitMQ";

    public string HostName { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string UserName { get; set; } = "guest";

    public string Password { get; set; } = "guest";
}