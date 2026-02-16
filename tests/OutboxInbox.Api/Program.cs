using Messaging.OutboxInbox;
using Messaging.OutboxInbox.AspNetCore.Extensions.DbContextExtensions;
using Messaging.OutboxInbox.AspNetCore.Extensions;
using Microsoft.EntityFrameworkCore;
using OutboxInbox.Api.Data;
using OutboxInbox.Api.Messages;
using OutboxInbox.Api.Models;

var builder = WebApplication.CreateBuilder(args);

// Add OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Test Messaging API", Version = "v1" });
});

builder.AddRabbitMQClient("rabbitmq");


////-- Only Outbox ---
//builder.AddNpgsqlDbContext<AppDbContext>("appdb",
//    configureDbContextOptions: options =>
//    {
//        options.IncludeOutboxMessaging(); // Only outbox
//    });

//// This registers ONLY outbox services
//builder.AddMessagingHandlers<AppDbContext>();
//// No handlers scanned, no inbox services

////---- Only Inbox -----
//builder.AddNpgsqlDbContext<AppDbContext>("appdb",
//    configureDbContextOptions: options =>
//    {
//        options.IncludeInboxMessaging(); //
//    });

//// This registers ONLY inbox services + handlers
//builder.AddMessagingHandlers<AppDbContext>(config =>
//{
//    config.AddSubscriber<ConversionCompletedMessage, ConversionCompletedMessageHandler>();
//});

////------Outbox and Inbox --------
builder.AddNpgsqlDbContext<AppDbContext>("appdb",
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

var app = builder.Build();


////temporary database just for testing
//using (var scope = app.Services.CreateScope())
//{
//    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
//    await dbContext.Database.EnsureDeletedAsync(); // Deletes existing DB
//    await dbContext.Database.EnsureCreatedAsync(); // Creates fresh schema
//}

//// WITH proper migrations:
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        if (app.Environment.IsDevelopment())
        {
            logger.LogWarning("🗑️  Dropping database for clean migration...");
            await dbContext.Database.EnsureDeletedAsync();
        }

        logger.LogInformation("🔄 Applying database migrations...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("✅ Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Error applying migrations");
        throw;
    }
}

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ============================================================================
// CONVERSION ENDPOINTS (PUBLISHER)
// ============================================================================

var conversions = app.MapGroup("/api/conversions").WithTags("Conversions");

conversions.MapPost("/", async (
    CreateConversionRequest request,
    AppDbContext dbContext,
    IMessagePublisher publisher,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    logger.LogInformation("📝 Creating conversion record for file: {FileName}", request.FileName);

    var startedAt = DateTime.UtcNow;

    // Simulate some processing time
    await Task.Delay(100, cancellationToken);

    var finishedAt = DateTime.UtcNow;

    // Create the conversion record
    var conversion = new ConversionRecord
    {
        DataSource = request.DataSource,
        FileName = request.FileName,
        FilePath = request.FilePath,
        ConvertedRecordsCount = request.ConvertedRecordsCount,
        TotalRecordCount = request.TotalRecordCount,
        StartedAt = startedAt,
        FinishedAt = finishedAt
    };

    dbContext.ConversionRecords.Add(conversion);

    // Create and publish the message
    var message = new ConversionCompletedMessage
    {
        ConversionId = conversion.Id,
        DataSource = conversion.DataSource,
        FileName = conversion.FileName,
        FilePath = conversion.FilePath,
        ConvertedRecordsCount = conversion.ConvertedRecordsCount,
        TotalRecordCount = conversion.TotalRecordCount,
        StartedAt = conversion.StartedAt,
        FinishedAt = conversion.FinishedAt
    };

    await publisher.PublishAsync(message, conversion.Id, cancellationToken);

    logger.LogInformation("📤 Message added to outbox for conversion {ConversionId}", conversion.Id);

    // Save changes - commits both the conversion AND the outbox record atomically
    await dbContext.SaveChangesAsync(cancellationToken);

    logger.LogInformation(
        "✅ Conversion {ConversionId} created and outbox record saved. Message will be published to RabbitMQ.",
        conversion.Id);

    return Results.Created($"/api/conversions/{conversion.Id}", new
    {
        ConversionId = conversion.Id,
        Message = "Conversion created successfully. Message queued for processing."
    });
})
.WithName("CreateConversion")
.Produces(StatusCodes.Status201Created);

conversions.MapGet("/", async (AppDbContext dbContext, CancellationToken cancellationToken) =>
{
    var conversions = await dbContext.ConversionRecords
        .OrderByDescending(c => c.StartedAt)
        .Take(50)
        .ToListAsync(cancellationToken);

    return Results.Ok(conversions);
})
.WithName("GetConversions");

conversions.MapGet("/{id}", async (Guid id, AppDbContext dbContext, CancellationToken cancellationToken) =>
{
    var conversion = await dbContext.ConversionRecords.FindAsync(new object[] { id }, cancellationToken);
    return conversion is null ? Results.NotFound() : Results.Ok(conversion);
})
.WithName("GetConversion");

// ============================================================================
// AUDIT LOG ENDPOINTS (SUBSCRIBER RESULTS)
// ============================================================================

var auditLogs = app.MapGroup("/api/audit-logs").WithTags("Audit Logs");

auditLogs.MapGet("/", async (AppDbContext dbContext, CancellationToken cancellationToken) =>
{
    var logs = await dbContext.ConversionAuditLogs
        .OrderByDescending(a => a.AuditedAt)
        .Take(50)
        .ToListAsync(cancellationToken);

    return Results.Ok(logs);
})
.WithName("GetAuditLogs");

auditLogs.MapGet("/conversion/{conversionId}", async (
    Guid conversionId,
    AppDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var log = await dbContext.ConversionAuditLogs
        .FirstOrDefaultAsync(a => a.ConversionId == conversionId, cancellationToken);

    return log is null ? Results.NotFound() : Results.Ok(log);
})
.WithName("GetAuditLogByConversion");


app.Run();

// ============================================================================
// REQUEST MODELS
// ============================================================================

public record CreateConversionRequest(
    string DataSource,
    string FileName,
    string FilePath,
    int ConvertedRecordsCount,
    int TotalRecordCount
);