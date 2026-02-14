namespace OutboxInbox.Api.Models;

public sealed class ConversionRecord
{
    public Guid Id { get; init; }
    public string DataSource { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int ConvertedRecordsCount { get; set; }
    public int TotalRecordCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
}
