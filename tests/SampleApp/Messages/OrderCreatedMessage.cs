using Messaging.OutboxInbox.Abstractions;
using System;

namespace SampleApp.Messages;

public sealed record OrderCreatedMessage : IMessage
{
    public Guid OrderId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
}