namespace OutboxInbox.Api.Models;

public sealed class ConversionAuditLog
{
    public Guid Id { get; init; }
    public Guid ConversionId { get; init; }
    public string DataSource { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int ConvertedRecordsCount { get; set; }
    public int TotalRecordCount { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime AuditedAt { get; init; }
    public string Notes { get; set; } = string.Empty;
}
