namespace Messaging.OutboxInbox.Entities;

public sealed class OutboxRecord
{
    public Guid Id { get; init; }
    public required string Type { get; init; }
    public required string Content { get; init; }
    public DateTime OccurredAt { get; init; }
    public DateTime? ProcessedAt { get; set; }
    public string? Error { get; set; }
}