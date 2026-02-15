namespace Messaging.OutboxInbox.Entities;

public sealed class OutboxRecord
{
    public OutboxRecord()
    {
        OccurredAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public required string Type { get; init; }
    
    public required string Content { get; init; }
    
    public DateTime OccurredAt { get; init; }
    
    public DateTime? ProcessedAt { get; set; }
    
    public string? Error { get; set; }
}