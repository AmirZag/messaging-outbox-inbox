using MediatR;

namespace Messaging.OutboxInbox.Abstractions;

/// <summary>
/// Base interface for all messages that can be sent through the messaging system.
/// Implements MediatR's IRequest for handling through the pipeline.
/// </summary>
public interface IMessage : IRequest
{
}
