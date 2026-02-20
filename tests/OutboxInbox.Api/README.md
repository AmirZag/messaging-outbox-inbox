# Messaging.OutboxInbox — Sample App

Demonstrates the Outbox + Inbox pattern using:
- .NET Aspire for infrastructure (PostgreSQL + RabbitMQ)
- A single API that publishes `ConversionCompletedMessage` via the Outbox
- The same API subscribes and processes it via the Inbox, creating an audit log

## Running
```bash
cd samples/OutboxInbox.Api.AppHost
dotnet run
```

Open the Aspire dashboard to see the API, Postgres (via pgAdmin), and RabbitMQ management UI.

## Flow

1. `POST /api/conversions` → saves `ConversionRecord` + `OutboxRecord` in one transaction 
{
  "dataSource": "Excel Import",
  "fileName": "customers_2024.xlsx",
  "filePath": "/uploads/customers_2024.xlsx",
  "convertedRecordsCount": 850,
  "totalRecordCount": 1000
}

2. `OutboxHostedService` polls, claims the record, publishes to RabbitMQ, marks as processed  
3. `RabbitMqSubscriber` receives it, writes to `InboxRecords`  
4. `InboxHostedService` processes it via `ConversionCompletedMessageHandler`, creates `ConversionAuditLog`  
5. `GET /api/audit-logs/conversion/{id}` confirms end-to-end delivery
{
    "id": "019c7a38-48ea-7173-9bbf-8d91da8d8971",
    "conversionId": "019c7a38-3e6e-76bc-90b3-4d99e17e4bea",
    "dataSource": "Excel Import",
    "fileName": "customers_2024.xlsx",
    "convertedRecordsCount": 850,
    "totalRecordCount": 1000,
    "successRate": 85,
    "duration": "00:00:00.1038340",
    "auditedAt": "2026-02-20T08:43:54.988109Z",
    "notes": "Completed in 0.10s with 85.0% success rate"
}