# Messaging.OutboxInbox

Core abstractions for reliable distributed messaging using the **Transactional Outbox & Inbox** patterns in .NET.

[![NuGet](https://img.shields.io/nuget/v/Messaging.OutboxInbox)](https://www.nuget.org/packages/Messaging.OutboxInbox)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/download)

> **Solution Architect:** Reza Noei · **Implementation:** Amirreza Ghasemi

---

## Overview

This package provides the foundational contracts for the Outbox/Inbox messaging pattern. It contains no infrastructure dependencies — just the interfaces your domain and application code depends on.

For the full ASP.NET Core integration (EF Core, RabbitMQ, hosted services), see [`Messaging.OutboxInbox.AspNetCore`](https://www.nuget.org/packages/Messaging.OutboxInbox.AspNetCore).

---

## Installation

```bash
dotnet add package Messaging.OutboxInbox
```

---

## What's Included

### `IMessage`

Base interface for all messages. Inherits from MediatR's `IRequest`, so every message is also dispatchable through the MediatR pipeline.

```csharp
public interface IMessage : IRequest { }
```

### `IMessageHandler<TMessage>`

Base interface for message handlers. Implement this to handle a specific message type on the consumer side.

```csharp
public interface IMessageHandler<in TMessage> : IRequestHandler<TMessage>
    where TMessage : IMessage { }
```

### `IMessagePublisher`

The entry point for publishing messages using the Outbox pattern. Call `PublishAsync` within the same scope as your `SaveChangesAsync` — the message is written atomically with your business data.

```csharp
public interface IMessagePublisher
{
    Task PublishAsync<TMessage>(TMessage message, Guid messageId, CancellationToken cancellationToken = default)
        where TMessage : IMessage;
}
```

> The `messageId` parameter is intentional — it ties the outbox record to your business entity's ID, enabling idempotent deduplication end-to-end.

---

## Usage

### 1. Define a Message

```csharp
using Messaging.OutboxInbox;

public sealed class OrderCreatedMessage : IMessage
{
    public Guid OrderId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
}
```

### 2. Implement a Handler

```csharp
using Messaging.OutboxInbox;

public sealed class OrderCreatedHandler : IMessageHandler<OrderCreatedMessage>
{
    private readonly AppDbContext _db;
    private readonly ILogger<OrderCreatedHandler> _logger;

    public OrderCreatedHandler(AppDbContext db, ILogger<OrderCreatedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Handle(OrderCreatedMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling OrderCreated for {OrderId}", message.OrderId);

        // Your business logic — e.g. send email, create audit log, update read model
        await _db.SaveChangesAsync(cancellationToken);
    }
}
```

### 3. Publish a Message

```csharp
app.MapPost("/orders", async (
    CreateOrderRequest req,
    AppDbContext db,
    IMessagePublisher publisher,
    CancellationToken ct) =>
{
    var order = new Order { Id = Guid.CreateVersion7(), ... };
    db.Orders.Add(order);

    // Called before SaveChangesAsync — part of the same transaction
    await publisher.PublishAsync(new OrderCreatedMessage
    {
        OrderId = order.Id,
        CustomerName = req.CustomerName,
        TotalAmount = req.TotalAmount
    }, order.Id, ct);

    await db.SaveChangesAsync(ct); // business data + outbox record saved atomically

    return Results.Created($"/orders/{order.Id}", order);
});
```

---

## Dependencies

| Package | Version |
|---|---|
| `MediatR` | 12.4.1 |

---

## Related Packages

| Package | Purpose |
|---|---|
| [`Messaging.OutboxInbox.AspNetCore`](https://www.nuget.org/packages/Messaging.OutboxInbox.AspNetCore) | EF Core, RabbitMQ, background services, DI registration |

---

## License

[MIT](https://opensource.org/licenses/MIT) © 2025 Resaa