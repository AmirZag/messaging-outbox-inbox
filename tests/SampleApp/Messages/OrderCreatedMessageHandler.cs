using Messaging.OutboxInbox.Abstractions;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace SampleApp.Messages;

public sealed class OrderCreatedMessageHandler : IMessageHandler<OrderCreatedMessage>
{
    private readonly ILogger<OrderCreatedMessageHandler> _logger;

    public OrderCreatedMessageHandler(ILogger<OrderCreatedMessageHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(OrderCreatedMessage request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("✅ ORDER PROCESSED: {OrderId} - {CustomerName} - ${Amount}",
            request.OrderId, request.CustomerName, request.TotalAmount);
        return Task.CompletedTask;
    }
}