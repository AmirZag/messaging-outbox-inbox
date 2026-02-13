// tests/SampleApp/Services/OrderService.cs
using Messaging.OutboxInbox.Abstractions;
using Microsoft.Extensions.Logging;
using SampleApp.Data;
using SampleApp.Entities;
using SampleApp.Messages;
using System;
using System.Threading.Tasks;

namespace SampleApp.Services;

public sealed class OrderService
{
    private readonly AppDbContext _context;
    private readonly IMessagePublisher _publisher;
    private readonly ILogger<OrderService> _logger;

    public OrderService(AppDbContext context, IMessagePublisher publisher, ILogger<OrderService> logger)
    {
        _context = context;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<Guid> CreateOrderAsync(string customerName, decimal totalAmount)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = customerName,
                TotalAmount = totalAmount,
                CreatedAt = DateTime.UtcNow
            };

            _context.Orders.Add(order);

            // PublishAsync adds to outbox and signals processor
            await _publisher.PublishAsync(new OrderCreatedMessage
            {
                OrderId = order.Id,
                CustomerName = order.CustomerName,
                TotalAmount = order.TotalAmount
            });

            // Save both order and outbox record atomically
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("📦 Order created: {OrderId}", order.Id);
            return order.Id;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}