# Messaging.OutboxInbox.AspNetCore

ASP.NET Core integration package for Messaging.OutboxInbox with RabbitMQ support and background processing.

## What's Included

- 🚀 **Background Hosted Services** - Automatic message processing
- 🐰 **RabbitMQ Integration** - Publisher and subscriber implementations
- ⚙️ **Configuration Extensions** - Simple builder pattern setup
- 📊 **Structured Logging** - Production-ready logging with EventIds
- 🔧 **EF Core Interceptors** - Automatic outbox message queuing

## Installation
```bash
dotnet add package Messaging.OutboxInbox.AspNetCore
```

This package automatically includes `Messaging.OutboxInbox` as a dependency.

## Configuration

### Minimal Setup
```csharp
builder.AddRabbitMQClient("rabbitmq");

// Add PostgreSQL with Aspire
builder.AddNpgsqlDbContext<AppDbContext>("appdb",
    configureDbContextOptions: options =>
    {
        options.IncludeOutboxMessaging();
        options.IncludeInboxMessaging();
    });

// Configure with handlers
builder.AddMessagingHandlers<AppDbContext>(config =>
{
    config.AddSubscriber<ConversionCreatedEvent, ConversionCreatedEventHandler>();
});
```

```csharp
------------------------------------------------------------------------------------------
Scenario 1: Outbox Only
csharpbuilder.AddNpgsqlDbContext<AppDbContext>("appdb",
    configureDbContextOptions: options =>
    {
        options.IncludeOutboxMessaging(); // Only outbox
    });

// This registers ONLY outbox services
builder.AddMessagingHandlers<AppDbContext>();
// No handlers scanned, no inbox services
------------------------------------------------------------------------------------------
Scenario 2: Inbox Only
csharpbuilder.AddNpgsqlDbContext<AppDbContext>("appdb",
    configureDbContextOptions: options =>
    {
        options.IncludeInboxMessaging(); // Only inbox
    });

// This registers ONLY inbox services + handlers
builder.AddMessagingHandlers<AppDbContext>(config =>
{
    config.AddSubscriber<ConversionCompletedMessage, ConversionCompletedMessageHandler>();
});

------------------------------------------------------------------------------------------
Scenario 3: Both
csharpbuilder.AddNpgsqlDbContext<AppDbContext>("appdb",
    configureDbContextOptions: options =>
    {
        options.IncludeOutboxMessaging();
        options.IncludeInboxMessaging();
    });

// This registers BOTH
builder.AddMessagingHandlers<AppDbContext>(config =>
{
    config.AddSubscriber<ConversionCompletedMessage, ConversionCompletedMessageHandler>();
});

------------------------------------------------------------------------------------------
Scenario 4: Error - Nothing Enabled
csharpbuilder.AddNpgsqlDbContext<AppDbContext>("appdb");

builder.AddMessagingHandlers<AppDbContext>();
// Throws: "No messaging features enabled. Please add..."
------------------------------------------------------------------------------------------
```
### Separate Services

**Publisher-only service:**
```csharp
builder.AddOutboxMessaging();
```

**Consumer-only service:**
```csharp
builder.AddInboxMessaging();
```

### appsettings.json
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
    "RoutingKey": "events"
  },
  "MessageSubscriber": {
    "ExchangeName": "messaging.events",
    "QueueName": "my-service.inbox.queue",
    "RoutingKey": "events",
    "PrefetchCount": 10
  },
  "Logging": {
    "LogLevel": {
      "Messaging.OutboxInbox": "Information"
    }
  }
}
```

## Components

### Hosted Services

- **OutboxHostedService** - Processes outbox messages and publishes to RabbitMQ
- **InboxHostedService** - Processes inbox messages via MediatR handlers
- **RabbitMqSubscriber** - Consumes messages from RabbitMQ

### Queues

- **IOutboxMessageQueue** - In-memory queue for outbox message buffering
- **IInboxMessageQueue** - In-memory queue for inbox message buffering

### Services

- **IOutboxMessagesService** - Database operations for outbox records
- **IInboxMessagesService** - Database operations for inbox records
- **RabbitMqPublisher** - RabbitMQ message publishing
- **MessagePublisher** - High-level message publishing API

## Database Schema

The package automatically creates two tables:

**OutboxRecords:**
- `Id` (PK) - Message identifier
- `Type` - Message type (assembly qualified name)
- `Content` - JSON serialized message
- `OccurredAt` - Timestamp
- `ProcessedAt` - Processing timestamp (null = unprocessed)
- `Error` - Error message if processing failed

**InboxRecords:**
- Same structure as OutboxRecords

## Production Considerations

### Logging

Set appropriate log levels:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Messaging.OutboxInbox": "Information"
    }
  }
}
```

Debug logs include method entry/exit for troubleshooting.

### Performance Tuning

**PrefetchCount:** Number of messages RabbitMQ delivers at once
```json
{
  "MessageSubscriber": {
    "PrefetchCount": 20  // Increase for higher throughput
  }
}
```

### Environment-Specific Configuration

Use different exchange names per environment:
```json
{
  "MessagePublisher": {
    "ExchangeName": "messaging.events.production"
  }
}
```

## Monitoring

All operations are logged with structured EventIds:

- **1xxx** - Outbox operations
- **2xxx** - Inbox operations  
- **3xxx** - RabbitMQ operations
- **4xxx** - Service lifecycle

Query logs by EventId for metrics and monitoring.

## Requirements

- ASP.NET Core 10.0+
- PostgreSQL with JSONB support
- RabbitMQ 7.0+
- Entity Framework Core 10.0+

## Dependencies

- `Messaging.OutboxInbox` (core library)
- `Microsoft.Extensions.Hosting.Abstractions`
- `Npgsql.EntityFrameworkCore.PostgreSQL`
- `RabbitMQ.Client`
- `Scrutor` (for decorator pattern)
- `MediatR` (for handler execution)

## License

MIT