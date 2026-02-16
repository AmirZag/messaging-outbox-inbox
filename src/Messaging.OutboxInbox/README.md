# Messaging.OutboxInbox

A reliable messaging library implementing the Transactional Outbox and Inbox patterns for distributed systems.

## Features

- 🔒 **Transactional Outbox Pattern** - Ensures messages are published atomically with your business transactions
- 📥 **Inbox Pattern** - Guarantees exactly-once message processing with idempotency
- 🎯 **MediatR Integration** - Seamless handler-based message processing
- 🔄 **Event Sourcing Ready** - Perfect for CQRS and event-driven architectures

## Installation
```bash
dotnet add package Messaging.OutboxInbox
dotnet add package Messaging.OutboxInbox.AspNetCore
```

## Quick Start

### 1. Define Your Messages
```csharp
using Messaging.OutboxInbox;

public record ConversionCreatedEvent : IMessage
{
    public Guid ConversionId { get; init; }
    public int TotalRecords { get; init; }
}
```

### 2. Configure Your DbContext
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

### 3. Publish Messages (Outbox)
```csharp
public class ConversionService
{
    private readonly MyDbContext _context;
    private readonly IMessagePublisher _publisher;
    
    public async Task CreateConversionAsync(CreateConversionRequest request)
    {
        var conversion = new Conversion { /* ... */ };
        _context.Conversions.Add(conversion);
        
        // Publish event atomically with the conversion creation
        await _publisher.PublishAsync(
            new ConversionCreatedEvent 
            { 
                ConversionId = conversion.Id, 
                TotalRecords = conversion.TotalRecords 
            },
            conversion.Id // Use conversion ID as message ID for idempotency
        );
        
        await _context.SaveChangesAsync(); // Both conversion and outbox record saved in one transaction
    }
}
```

### 4. Handle Messages (Inbox)
```csharp
using Messaging.OutboxInbox;

public class ConversionCreatedEventHandler : IMessageHandler
{
    private readonly ILogger _logger;
    
    public async Task Handle(ConversionCreatedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing Conversion {ConversionId}", message.ConversionId);
        
        // Your business logic here
        // This will only execute once per message (idempotent)
    }
}
```

### 5. Configure in Program.cs
```csharp
var builder = WebApplication.CreateBuilder(args);

// Add database
builder.Services.AddDbContext(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Option A: Add only Outbox (for publishers)
builder.AddOutboxMessaging();

// Option B: Add only Inbox (for consumers)
builder.AddInboxMessaging();

// Option C: Add both Outbox + Inbox + Handlers (full setup)
builder.AddMessagingHandlers();
```

### 6. Configure appsettings.json
```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Database=mydb;Username=postgres;Password=postgres"
  },
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
    "QueueName": "my-service.queue",
    "RoutingKey": "events",
    "PrefetchCount": 10
  }
}
```

### 7. Run Migrations
```bash
dotnet ef migrations add AddOutboxInbox
dotnet ef database update
```

## How It Works

### Outbox Pattern
1. Your code publishes a message via `IMessagePublisher`
2. Message is saved to `OutboxRecords` table in the **same transaction** as your business data
3. Background service reads unprocessed outbox records
4. Messages are published to RabbitMQ
5. Successfully published messages are marked as processed

### Inbox Pattern
1. RabbitMQ delivers message to your service
2. Message is saved to `InboxRecords` table (with duplicate check)
3. Background service processes messages via MediatR handlers
4. Successfully processed messages are marked as processed
5. Duplicate messages are automatically ignored (idempotency)

## Key Guarantees

✅ **Exactly-once publishing** - Messages published atomically with your transaction  
✅ **Exactly-once processing** - Duplicate messages automatically deduplicated  
✅ **Ordering** - Messages processed in order of occurrence  
✅ **Reliability** - Survives crashes and restarts  
✅ **Idempotency** - Safe to retry operations

## Advanced Usage

### Cross-Assembly Handlers
```csharp
builder.AddMessagingHandlers(config =>
{
    config.AddSubscriber();
    config.AddSubscriber();
});
```

### Separate Publisher and Consumer Services

**Publisher Service:**
```csharp
builder.AddOutboxMessaging();
```

**Consumer Service:**
```csharp
builder.AddInboxMessaging();
```

## Requirements

- .NET 10.0+
- PostgreSQL (with JSONB support)
- RabbitMQ
- Entity Framework Core 10.0+

## License

MIT

## Support

For issues and questions, please open an issue on GitHub.