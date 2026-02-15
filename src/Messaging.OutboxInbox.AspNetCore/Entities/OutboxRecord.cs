namespace Messaging.OutboxInbox.Entities;

public sealed class OutboxRecord
{
    public OutboxRecord()
    {
        OccurredAt = DateTime.UtcNow;
    }

    // TODO: [Value Generated On Add]
    public Guid Id { get; private set; }

    public required string Type { get; init; }
    
    public required string Content { get; init; }
    
    public DateTime OccurredAt { get; init; }
    
    public DateTime? ProcessedAt { get; set; }
    
    public string? Error { get; set; }
}