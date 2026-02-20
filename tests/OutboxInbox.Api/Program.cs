using Messaging.OutboxInbox;
using Messaging.OutboxInbox.AspNetCore.Extensions;
using Messaging.OutboxInbox.AspNetCore.Extensions.DbContextExtensions;
using Microsoft.EntityFrameworkCore;
using OutboxInbox.Api.Data;
using OutboxInbox.Api.Messages;
using OutboxInbox.Api.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "Messaging.OutboxInbox Sample API", Version = "v1" }));

builder.AddRabbitMQClient("rabbitmq");

// Register DbContext with both Outbox and Inbox support.
// You can also use .IncludeOutboxMessaging() or .IncludeInboxMessaging() independently.
builder.AddNpgsqlDbContext<AppDbContext>("appdb",
    configureDbContextOptions: options =>
    {
        options.IncludeOutboxMessaging();
        options.IncludeInboxMessaging();
    });

builder.AddMessagingHandlers<AppDbContext>(config =>
{
    config.AddSubscriber<ConversionCompletedMessage, ConversionCompletedMessageHandler>();
});

var app = builder.Build();

// Apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        if (app.Environment.IsDevelopment())
            await dbContext.Database.EnsureDeletedAsync(); // fresh schema each run in dev

        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error applying migrations");
        throw;
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Conversions
var conversions = app.MapGroup("/api/conversions").WithTags("Conversions");

conversions.MapPost("/", async (
    CreateConversionRequest request,
    AppDbContext dbContext,
    IMessagePublisher publisher,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var startedAt = DateTime.UtcNow;
    await Task.Delay(100, cancellationToken); // simulate work
    var finishedAt = DateTime.UtcNow;

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

    await publisher.PublishAsync(new ConversionCompletedMessage
    {
        ConversionId = conversion.Id,
        DataSource = conversion.DataSource,
        FileName = conversion.FileName,
        FilePath = conversion.FilePath,
        ConvertedRecordsCount = conversion.ConvertedRecordsCount,
        TotalRecordCount = conversion.TotalRecordCount,
        StartedAt = conversion.StartedAt,
        FinishedAt = conversion.FinishedAt
    }, conversion.Id, cancellationToken);

    await dbContext.SaveChangesAsync(cancellationToken);

    logger.LogInformation("Conversion {ConversionId} created and outbox record saved", conversion.Id);

    return Results.Created($"/api/conversions/{conversion.Id}", new
    {
        conversion.Id,
        Message = "Conversion created. Message queued for publishing."
    });
})
.WithName("CreateConversion")
.Produces(StatusCodes.Status201Created);

conversions.MapGet("/", async (AppDbContext dbContext, CancellationToken cancellationToken) =>
    Results.Ok(await dbContext.ConversionRecords
        .OrderByDescending(c => c.StartedAt).Take(50)
        .ToListAsync(cancellationToken)))
.WithName("GetConversions");

conversions.MapGet("/{id:guid}", async (Guid id, AppDbContext dbContext, CancellationToken cancellationToken) =>
{
    var conversion = await dbContext.ConversionRecords.FindAsync([id], cancellationToken);
    return conversion is null ? Results.NotFound() : Results.Ok(conversion);
})
.WithName("GetConversion");

// Audit Logs
var auditLogs = app.MapGroup("/api/audit-logs").WithTags("Audit Logs");

auditLogs.MapGet("/", async (AppDbContext dbContext, CancellationToken cancellationToken) =>
    Results.Ok(await dbContext.ConversionAuditLogs
        .OrderByDescending(a => a.AuditedAt).Take(50)
        .ToListAsync(cancellationToken)))
.WithName("GetAuditLogs");

auditLogs.MapGet("/conversion/{conversionId:guid}", async (
    Guid conversionId, AppDbContext dbContext, CancellationToken cancellationToken) =>
{
    var log = await dbContext.ConversionAuditLogs
        .FirstOrDefaultAsync(a => a.ConversionId == conversionId, cancellationToken);
    return log is null ? Results.NotFound() : Results.Ok(log);
})
.WithName("GetAuditLogByConversion");

app.Run();

public record CreateConversionRequest(
    string DataSource,
    string FileName,
    string FilePath,
    int ConvertedRecordsCount,
    int TotalRecordCount);