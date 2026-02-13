namespace Messaging.OutboxInbox.AspNetCore.Options;

public sealed class OutboxProcessorOptions
{
    public const string SectionName = "OutboxProcessor";

    public int BatchSize { get; set; } = 100;
    public int PollIntervalSeconds { get; set; } = 5;
    public int ErrorRetryDelaySeconds { get; set; } = 5;
    public int MaxRetryAttempts { get; set; } = 3;
}