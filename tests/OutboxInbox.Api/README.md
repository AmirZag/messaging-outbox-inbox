# OutboxInbox Sample Application

A fully working end-to-end sample demonstrating the **Transactional Outbox & Inbox** patterns using [`Messaging.OutboxInbox.AspNetCore`](https://www.nuget.org/packages/Messaging.OutboxInbox.AspNetCore), .NET Aspire, PostgreSQL, and RabbitMQ.

[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/download)
[![Aspire](https://img.shields.io/badge/.NET_Aspire-13.1-blue)](https://learn.microsoft.com/en-us/dotnet/aspire/)

---

## What This Sample Shows

A single API service (`OutboxInbox.Api`) acts as both publisher and consumer to demonstrate the full round-trip:

1. A `POST /api/conversions` request creates a `ConversionRecord` and publishes a `ConversionCompletedMessage` via the **Outbox**.
2. The background `OutboxHostedService` picks up the message and publishes it to RabbitMQ.
3. The `RabbitMqSubscriber` receives it, writes an `InboxRecord` to the database, and enqueues it.
4. The `InboxHostedService` dispatches it via MediatR to `ConversionCompletedMessageHandler`.
5. The handler creates a `ConversionAuditLog` record — verifiable via `GET /api/audit-logs`.

```
POST /api/conversions
        │
        ▼
  ConversionRecord ──┐
  OutboxRecord  ─────┘  (same transaction via SaveChangesAsync)
        │
        ▼
  OutboxHostedService
        │
        ▼
    RabbitMQ Exchange ──► RabbitMqSubscriber
                                │
                                ▼
                          InboxRecord (idempotent insert)
                                │
                                ▼
                        InboxHostedService
                                │
                                ▼
              ConversionCompletedMessageHandler
                                │
                                ▼
                       ConversionAuditLog ✓
```

---

## Projects

```
samples/
├── OutboxInbox.Api/        # ASP.NET Core Minimal API
│   ├── Data/
│   │   └── AppDbContext.cs             # EF Core DbContext
│   ├── Messages/
│   │   ├── ConversionCompletedMessage.cs        # IMessage implementation
│   │   └── ConversionCompletedMessageHandler.cs # IMessageHandler implementation
│   ├── Models/
│   │   ├── ConversionRecord.cs         # Business entity
│   │   └── ConversionAuditLog.cs       # Audit entity written by handler
│   ├── Program.cs                      # Minimal API + DI setup
│   └── appsettings.json                # RabbitMQ + messaging config
│
└── OutboxInbox.AppHost/    # .NET Aspire orchestration
    └── Program.cs          # PostgreSQL + RabbitMQ + API wiring
```

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for PostgreSQL and RabbitMQ containers)
- [.NET Aspire workload](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/setup-tooling)

```bash
dotnet workload install aspire
```

---

## Running the Sample

```bash
git clone https://github.com/AmirZag/messaging-outbox-inbox
cd messaging-outbox-inbox/samples/OutboxInbox.AppHost
dotnet run
```

Aspire will automatically:
- Start a **PostgreSQL** container (with pgAdmin)
- Start a **RabbitMQ** container (with Management UI)
- Apply database migrations
- Start the API

Open the **Aspire Dashboard** URL printed in the console to see all services, logs, and traces.

---

## API Endpoints

### Conversions

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/conversions` | Create a conversion — triggers the full Outbox → Inbox flow |
| `GET` | `/api/conversions` | List last 50 conversions |
| `GET` | `/api/conversions/{id}` | Get a specific conversion |

### Audit Logs

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/audit-logs` | List last 50 audit logs (written by the Inbox handler) |
| `GET` | `/api/audit-logs/conversion/{conversionId}` | Get the audit log for a specific conversion |

Swagger UI is available at `/swagger` in development.

---

## Try It

```bash
# 1. Create a conversion (triggers the full flow)
curl -X POST http://localhost:<port>/api/conversions \
  -H "Content-Type: application/json" \
  -d '{
    "dataSource": "ERP",
    "fileName": "export.csv",
    "filePath": "/data/export.csv",
    "convertedRecordsCount": 950,
    "totalRecordCount": 1000
  }'

# 2. Check the audit log was created by the Inbox handler
curl http://localhost:<port>/api/audit-logs
```

The audit log will show the calculated `SuccessRate` (95%) and `Duration` — values computed entirely inside the `ConversionCompletedMessageHandler`, proving the message was received and processed end-to-end.

---

## Key Code Walkthrough

### Registering Messaging (Program.cs)

```csharp
// DbContext with both Outbox and Inbox enabled
builder.AddNpgsqlDbContext<AppDbContext>("appdb",
    configureDbContextOptions: options =>
    {
        options.IncludeOutboxMessaging();
        options.IncludeInboxMessaging();
    });

// Single call wires up everything
builder.AddMessagingHandlers<AppDbContext>(config =>
{
    config.AddSubscriber<ConversionCompletedMessage, ConversionCompletedMessageHandler>();
});
```

### Publishing (Program.cs — POST endpoint)

```csharp
// Both the business entity and the outbox record are saved atomically
dbContext.ConversionRecords.Add(conversion);

await publisher.PublishAsync(new ConversionCompletedMessage { ... }, conversion.Id, ct);

await dbContext.SaveChangesAsync(ct); // one transaction, both records
```

### Handling (ConversionCompletedMessageHandler.cs)

```csharp
public sealed class ConversionCompletedMessageHandler : IMessageHandler<ConversionCompletedMessage>
{
    public async Task Handle(ConversionCompletedMessage message, CancellationToken cancellationToken)
    {
        var auditLog = new ConversionAuditLog
        {
            ConversionId = message.ConversionId,
            SuccessRate = (double)message.ConvertedRecordsCount / message.TotalRecordCount * 100,
            Duration = message.FinishedAt - message.StartedAt,
            // ...
        };

        _dbContext.ConversionAuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

### Aspire Orchestration (AppHost/Program.cs)

```csharp
var appDb = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithDataVolume()
    .AddDatabase("appdb");

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithDataVolume()
    .WithManagementPlugin();

builder.AddProject<Projects.OutboxInbox_Api>("api")
    .WithReference(appDb).WaitFor(appDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithExternalHttpEndpoints();
```

---

## Configuration

**`appsettings.json`**

```json
{
  "MessagePublisher": {
    "ExchangeName": "messaging.events",
    "RoutingKey": "conversion.events"
  },
  "MessageSubscriber": {
    "ExchangeName": "messaging.events",
    "QueueName": "conversion.audit.queue",
    "RoutingKey": "conversion.events",
    "PrefetchCount": 10
  }
}
```

RabbitMQ connection details (`HostName`, `Port`, `UserName`, `Password`) are injected automatically by .NET Aspire via the `AddRabbitMQClient` integration — no manual config needed when running through Aspire.

---

## License

[MIT](https://opensource.org/licenses/MIT) © 2025 Resaa