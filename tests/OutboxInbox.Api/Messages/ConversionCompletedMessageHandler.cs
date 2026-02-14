using Messaging.OutboxInbox.Abstractions;
using OutboxInbox.Api.Data;
using OutboxInbox.Api.Models;

namespace OutboxInbox.Api.Messages;

/// <summary>
/// Handler for ConversionCompletedMessage - creates audit logs
/// </summary>
public sealed class ConversionCompletedMessageHandler : IMessageHandler<ConversionCompletedMessage>
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ConversionCompletedMessageHandler> _logger;

    public ConversionCompletedMessageHandler(
        AppDbContext dbContext,
        ILogger<ConversionCompletedMessageHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Handle(ConversionCompletedMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "🎯 HANDLER INVOKED! Processing ConversionCompletedMessage for Conversion {ConversionId}",
            message.ConversionId);

        _logger.LogInformation(
            "📊 Conversion Details: DataSource={DataSource}, File={FileName}, Converted={Converted}/{Total}",
            message.DataSource,
            message.FileName,
            message.ConvertedRecordsCount,
            message.TotalRecordCount);

        // Calculate metrics
        var duration = message.FinishedAt - message.StartedAt;
        var successRate = message.TotalRecordCount > 0
            ? (double)message.ConvertedRecordsCount / message.TotalRecordCount * 100
            : 0;

        // Create audit log
        var auditLog = new ConversionAuditLog
        {
            Id = Guid.NewGuid(),
            ConversionId = message.ConversionId,
            DataSource = message.DataSource,
            FileName = message.FileName,
            ConvertedRecordsCount = message.ConvertedRecordsCount,
            TotalRecordCount = message.TotalRecordCount,
            SuccessRate = successRate,
            Duration = duration,
            AuditedAt = DateTime.UtcNow,
            Notes = $"Conversion completed in {duration.TotalSeconds:F2} seconds with {successRate:F1}% success rate"
        };

        _dbContext.ConversionAuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "✅ SUCCESS! Audit log created for Conversion {ConversionId}. Success Rate: {SuccessRate:F1}%, Duration: {Duration:F2}s",
            message.ConversionId,
            successRate,
            duration.TotalSeconds);
    }
}
