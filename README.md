# Messaging.OutboxInbox

Reliable distributed messaging for .NET using the **Transactional Outbox & Inbox** patterns — ensuring exactly-once message delivery with PostgreSQL, RabbitMQ, and MediatR.

> **Solution Architect:** Reza Noei · **Implementation:** Amirreza Ghasemi

---

## Packages

| Package | NuGet | Description |
|---|---|---|
| `Messaging.OutboxInbox` | [![NuGet](https://img.shields.io/nuget/v/Messaging.OutboxInbox)](https://www.nuget.org/packages/Messaging.OutboxInbox) | Core abstractions — `IMessage`, `IMessageHandler`, `IMessagePublisher` |
| `Messaging.OutboxInbox.AspNetCore` | [![NuGet](https://img.shields.io/nuget/v/Messaging.OutboxInbox.AspNetCore)](https://www.nuget.org/packages/Messaging.OutboxInbox.AspNetCore) | ASP.NET Core integration — EF Core, RabbitMQ, background services |

---

## Why This Exists

In distributed systems, calling `SaveChanges()` and then publishing to a message broker in two separate steps risks losing messages if the app crashes between them. The **Transactional Outbox** pattern solves this by writing the message *inside* the same database transaction as your business data. A background worker then picks it up and publishes it to RabbitMQ reliably.

The **Inbox** pattern mirrors this on the consumer side — messages are persisted before processing, preventing duplicate handling on retries.

```
[Your Service]                     [RabbitMQ]             [Consumer Service]
   │                                   │                         │
   ├─ Save business data ──────────────┼─────────────────────────┤
   ├─ Save OutboxRecord (same tx) ─────┼─────────────────────────┤
   │                                   │                         │
[OutboxHostedService]                  │                         │
   ├─ Reads unprocessed OutboxRecords  │                         │
   └─ Publishes to RabbitMQ ──────────►│                         │
                                       │      [RabbitMqSubscriber]│
                                       │◄─────────────────────────┤
                                       │     Save InboxRecord     │
                                       │  [InboxHostedService]    │
                                       │     Dispatch via MediatR │
```

---

## Installation

```bash
# Core abstractions (required)
dotnet add package Messaging.OutboxInbox

# ASP.NET Core integration (required for background processing)
dotnet add package Messaging.OutboxInbox.AspNetCore
```

---

## Quick Start

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

### 2. Create a Handler

```csharp
using Messaging.OutboxInbox;

public sealed class OrderCreatedHandler : IMessageHandler<OrderCreatedMessage>
{
    private readonly AppDbContext _db;

    public OrderCreatedHandler(AppDbContext db) => _db = db;

    public async Task Handle(OrderCreatedMessage message, CancellationToken cancellationToken)
    {
        // Your business logic here — e.g. create audit log, trigger notifications
        await _db.SaveChangesAsync(cancellationToken);
    }
}
```

### 3. Configure Your DbContext

```csharp
// Option A: Inherit from OutboxInboxContext (recommended)
public class AppDbContext : OutboxInboxContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
}
```

Or if you prefer a plain `DbContext`, configure the EF Core extensions manually:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString)
           .IncludeOutboxMessaging()   // adds OutboxRecords table
           .IncludeInboxMessaging();   // adds InboxRecords table
});
```

> Use `.IncludeOutboxMessaging()` or `.IncludeInboxMessaging()` independently if only one side is needed.

### 4. Register Messaging in Program.cs

```csharp
builder.AddMessagingHandlers<AppDbContext>(config =>
{
    config.AddSubscriber<OrderCreatedMessage, OrderCreatedHandler>();
});
```

This single call auto-detects which features (Outbox/Inbox) are enabled in your DbContext and wires up everything accordingly — RabbitMQ connection, background hosted services, MediatR, and handler scanning.

### 5. Publish a Message

Call `PublishAsync` inside the same scope as your `SaveChangesAsync`. The message is written atomically with your business data.

```csharp
app.MapPost("/orders", async (
    CreateOrderRequest req,
    AppDbContext db,
    IMessagePublisher publisher,
    CancellationToken ct) =>
{
    var order = new Order { Id = Guid.CreateVersion7(), ... };
    db.Orders.Add(order);

    // Publish is part of the same transaction
    await publisher.PublishAsync(new OrderCreatedMessage
    {
        OrderId = order.Id,
        CustomerName = req.CustomerName,
        TotalAmount = req.TotalAmount
    }, order.Id, ct);

    await db.SaveChangesAsync(ct); // outbox record saved atomically

    return Results.Created($"/orders/{order.Id}", order);
});
```

### 6. Configuration (appsettings.json)

```json
{
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest"
  },
  "MessagePublisher": {
    "ExchangeName": "messaging.events",
    "RoutingKey": "order.events"
  },
  "MessageSubscriber": {
    "ExchangeName": "messaging.events",
    "QueueName": "order.processing.queue",
    "RoutingKey": "order.events",
    "PrefetchCount": 10
  }
}
```

### 7. Run Migrations

```bash
dotnet ef migrations add AddMessagingTables
dotnet ef database update
```

---

## How It Works

### Outbox Flow

1. `IMessagePublisher.PublishAsync` writes an `OutboxRecord` to your database — inside the same transaction as your business data.
2. An EF Core `SaveChangesInterceptor` (`OutboxEnqueueInterceptor`) captures the record immediately after `SaveChanges` and pushes it to an in-memory `OutboxMessageQueue`.
3. `OutboxHostedService` dequeues records and publishes them to RabbitMQ via `RabbitMqPublisher`.
4. On startup, any unprocessed records (from a previous crash) are loaded from the DB and re-queued automatically.

### Inbox Flow

1. `RabbitMqSubscriber` (an `IHostedService`) listens on the configured queue and writes each received message to an `InboxRecord` in the database — idempotently (duplicate messages are silently skipped using PostgreSQL's unique constraint).
2. The record is pushed to the in-memory `InboxMessageQueue`.
3. `InboxHostedService` dequeues records and dispatches them via MediatR to your `IMessageHandler<T>` implementation.
4. On startup, unprocessed inbox records are also re-loaded and queued.

---

## Advanced Usage

### Outbox Only (Publishing service)

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(conn).IncludeOutboxMessaging());

builder.AddOutboxMessaging<AppDbContext>();
```

### Inbox Only (Consuming service)

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(conn).IncludeInboxMessaging());

builder.AddInboxMessaging<AppDbContext>();
```

### Auto-scanning vs. Explicit Registration

`AddMessagingHandlers` scans the assembly of your `TContext` for all `IMessageHandler<T>` implementations automatically. You can also register specific handlers explicitly:

```csharp
builder.AddMessagingHandlers<AppDbContext>(config =>
{
    config.AddSubscriber<OrderCreatedMessage, OrderCreatedHandler>();
    config.AddSubscriber<PaymentProcessedMessage, PaymentProcessedHandler>();
});
```

---

## Database Tables

Two tables are created automatically:

**OutboxRecords**

| Column | Type | Notes |
|---|---|---|
| `Id` | `uuid` | Message ID (matches your entity ID) |
| `Type` | `varchar(2000)` | Assembly-qualified type name |
| `Content` | `jsonb` | Serialized message payload |
| `OccurredAt` | `timestamp` | When the record was created |
| `ProcessedAt` | `timestamp?` | Set when published to RabbitMQ |
| `Error` | `varchar(2000)?` | Set on failure |

**InboxRecords** — same schema, `ProcessedAt` set when the handler completes.

Both tables have a partial index on `ProcessedAt IS NULL` for efficient unprocessed-record queries.

---

## Sample App

The `samples/` folder contains a fully working .NET Aspire application demonstrating end-to-end usage.

```
samples/
├── OutboxInbox.Api/        # ASP.NET Core Minimal API (Outbox + Inbox in one service)
└── OutboxInbox.AppHost/    # .NET Aspire orchestration (PostgreSQL + RabbitMQ + API)
```

### Running the Sample

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Docker](https://www.docker.com/).

```bash
git clone https://github.com/AmirZag/messaging-outbox-inbox
cd messaging-outbox-inbox/samples/OutboxInbox.AppHost
dotnet run
```

Aspire will spin up PostgreSQL (with pgAdmin) and RabbitMQ (with Management UI) automatically. The API will be available at the URL shown in the Aspire dashboard.

**Try it out:**

```bash
# Create a conversion (triggers Outbox → RabbitMQ → Inbox → Handler → AuditLog)
curl -X POST http://localhost:<port>/api/conversions \
  -H "Content-Type: application/json" \
  -d '{"dataSource":"ERP","fileName":"export.csv","filePath":"/data/export.csv","convertedRecordsCount":950,"totalRecordCount":1000}'

# Check the audit log was created by the Inbox handler
curl http://localhost:<port>/api/audit-logs
```

---

## Requirements

- .NET 10+
- PostgreSQL
- RabbitMQ

---

## Contributing

Pull requests are welcome. For major changes, please open an issue first to discuss what you'd like to change.

---

## License

[MIT](LICENSE) © 2025 Resaa