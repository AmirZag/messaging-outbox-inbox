using Messaging.OutboxInbox;

namespace OutboxInbox.Api.Messages;

public sealed class ConversionCompletedMessage : IMessage
{
    public Guid ConversionId { get; init; }
    public string DataSource { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int ConvertedRecordsCount { get; init; }
    public int TotalRecordCount { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime FinishedAt { get; init; }
}
