using Messaging.OutboxInbox;
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
        _logger.LogInformation("Start Processing ConversionCompletedMessage for Conversion {ConversionId}", message.ConversionId);

        var duration = message.FinishedAt - message.StartedAt;
        var successRate = message.TotalRecordCount > 0
            ? (double)message.ConvertedRecordsCount / message.TotalRecordCount * 100
            : 0;

        var auditLog = new ConversionAuditLog
        {
            Id = Guid.CreateVersion7(),
            ConversionId = message.ConversionId,
            DataSource = message.DataSource,
            FileName = message.FileName,
            ConvertedRecordsCount = message.ConvertedRecordsCount,
            TotalRecordCount = message.TotalRecordCount,
            SuccessRate = successRate,
            Duration = duration,
            AuditedAt = DateTime.UtcNow,
            Notes = $"Completed in {duration.TotalSeconds:F2}s with {successRate:F1}% success rate"
        };

        _dbContext.ConversionAuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("========= [AUDIT] Conversion created. ConversionId={ConversionId} =========", message.ConversionId);
    }
}
