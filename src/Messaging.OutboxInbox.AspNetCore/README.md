# Messaging.OutboxInbox.AspNetCore

ASP.NET Core integration for the Transactional Outbox & Inbox patterns — wiring up EF Core, RabbitMQ, background hosted services, and automatic message processing.

[![NuGet](https://img.shields.io/nuget/v/Messaging.OutboxInbox.AspNetCore)](https://www.nuget.org/packages/Messaging.OutboxInbox.AspNetCore)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/download)

> **Solution Architect:** Reza Noei · **Implementation:** Amirreza Ghasemi

---

## Overview

This package builds on top of [`Messaging.OutboxInbox`](https://www.nuget.org/packages/Messaging.OutboxInbox) to provide everything needed to run the Outbox/Inbox pattern in a real ASP.NET Core application:

- **EF Core model customization** — automatically adds `OutboxRecords` and `InboxRecords` tables to your existing `DbContext`
- **EF Core interceptor** — captures saved `OutboxRecord` entries and enqueues them immediately after `SaveChanges`
- **RabbitMQ publisher** — reliably publishes outbox messages to a configured exchange
- **RabbitMQ subscriber** — consumes messages from a queue and writes them to the inbox idempotently
- **Background hosted services** — `OutboxHostedService` and `InboxHostedService` process messages continuously
- **Startup recovery** — unprocessed records are reloaded from the database on startup after a crash

---

## Installation

```bash
dotnet add package Messaging.OutboxInbox.AspNetCore
```

---

## Setup

### 1. Configure Your DbContext

Extend `OutboxInboxContext` (recommended — handles everything automatically):

```csharp
public class AppDbContext : OutboxInboxContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
}
```

Or use a plain `DbContext` with the EF Core extension methods:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString)
           .IncludeOutboxMessaging()   // registers OutboxRecords + interceptor
           .IncludeInboxMessaging();   // registers InboxRecords
});
```

Use them independently if your service only publishes or only consumes:

```csharp
// Publishing service only
options.UseNpgsql(conn).IncludeOutboxMessaging();

// Consuming service only
options.UseNpgsql(conn).IncludeInboxMessaging();
```

### 2. Register Messaging Services

```csharp
// Recommended: auto-detects Outbox/Inbox from DbContext config and wires everything
builder.AddMessagingHandlers<AppDbContext>(config =>
{
    config.AddSubscriber<OrderCreatedMessage, OrderCreatedHandler>();
});
```

Or register each side independently:

```csharp
builder.AddOutboxMessaging<AppDbContext>();   // outbox + RabbitMQ publisher + hosted service
builder.AddInboxMessaging<AppDbContext>();    // inbox + RabbitMQ subscriber + hosted service
```

### 3. Configuration

Add the following to `appsettings.json`:

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

### 4. Apply Migrations

```bash
dotnet ef migrations add AddMessagingTables
dotnet ef database update
```

---

## How It Works

### Outbox Pipeline

```
Your Code                    EF Core                  Background
─────────────────────────────────────────────────────────────────
publisher.PublishAsync()  →  OutboxRecord added
db.SaveChangesAsync()     →  OutboxEnqueueInterceptor  →  OutboxMessageQueue
                                                        →  OutboxHostedService
                                                        →  RabbitMqPublisher
                                                        →  RabbitMQ Exchange
```

1. `IMessagePublisher.PublishAsync` adds an `OutboxRecord` to the EF Core change tracker.
2. `SaveChangesAsync` persists both your business entity and the outbox record atomically.
3. `OutboxEnqueueInterceptor` captures the saved record and pushes it to the in-memory `OutboxMessageQueue`.
4. `OutboxHostedService` dequeues and publishes to RabbitMQ, then marks the record as processed.

### Inbox Pipeline

```
RabbitMQ Queue               Background                  Your Handler
─────────────────────────────────────────────────────────────────────
Message arrives           →  RabbitMqSubscriber
                          →  TryInsertAsync (idempotent)
                          →  InboxMessageQueue
                          →  InboxHostedService
                          →  MediatR.Send()             →  IMessageHandler<T>
```

1. `RabbitMqSubscriber` receives the message and writes an `InboxRecord` to the database — duplicate messages are silently ignored via a unique constraint.
2. The record is pushed to the in-memory `InboxMessageQueue`.
3. `InboxHostedService` dequeues and dispatches via MediatR to your `IMessageHandler<T>`.
4. The record is marked as processed.

### Startup Recovery

On startup, both hosted services query the database for records where `ProcessedAt IS NULL` and re-enqueue them. This ensures no messages are lost after an application crash.

---

## Configuration Reference

### `RabbitMQ` section

| Key | Default | Description |
|---|---|---|
| `HostName` | `localhost` | RabbitMQ server hostname |
| `Port` | `5672` | RabbitMQ port |
| `UserName` | `guest` | Username |
| `Password` | `guest` | Password |

### `MessagePublisher` section

| Key | Default | Description |
|---|---|---|
| `ExchangeName` | `messaging.events` | Exchange to publish to |
| `RoutingKey` | `events` | Routing key for published messages |

### `MessageSubscriber` section

| Key | Default | Description |
|---|---|---|
| `ExchangeName` | `messaging.events` | Exchange to bind to |
| `QueueName` | `inbox.queue` | Queue to consume from |
| `RoutingKey` | `events` | Binding routing key |
| `PrefetchCount` | `10` | RabbitMQ QoS prefetch count |

---

## Database Tables

Both tables are added automatically when you call `.IncludeOutboxMessaging()` / `.IncludeInboxMessaging()`.

**`OutboxRecords`** and **`InboxRecords`** share the same schema:

| Column | Type | Notes |
|---|---|---|
| `Id` | `uuid` | Message ID — matches your business entity ID |
| `Type` | `varchar(2000)` | Assembly-qualified type name |
| `Content` | `jsonb` | JSON-serialized message payload |
| `OccurredAt` | `timestamp` | Record creation time (UTC) |
| `ProcessedAt` | `timestamp?` | Set when successfully processed |
| `Error` | `varchar(2000)?` | Set when processing fails |

A partial index on `"ProcessedAt" IS NULL` is created on both tables for efficient polling queries.

---

## Dependencies

| Package | Version |
|---|---|
| `Messaging.OutboxInbox` | 1.0.0 |
| `Microsoft.Extensions.Hosting.Abstractions` | 10.0.3 |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | 10.0.3 |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.0 |
| `RabbitMQ.Client` | 7.2.0 |
| `Scrutor` | 7.0.0 |

---

## Related Packages

| Package | Purpose |
|---|---|
| [`Messaging.OutboxInbox`](https://www.nuget.org/packages/Messaging.OutboxInbox) | Core abstractions — `IMessage`, `IMessageHandler`, `IMessagePublisher` |

---

## License

[MIT](https://opensource.org/licenses/MIT) © 2025 Resaa