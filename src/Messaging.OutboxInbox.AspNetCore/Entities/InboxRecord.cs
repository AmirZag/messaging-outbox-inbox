namespace Messaging.OutboxInbox.Entities;

public class InboxRecord
{
    public Guid Id { get; init; }

    public required string Type { get; init; }
    
    public required string Content { get; init; }
    
    public DateTime OccurredAt { get; init; }
    
    public DateTime? ProcessedAt { get; set; }
    
    public string? Error { get; set; }
}