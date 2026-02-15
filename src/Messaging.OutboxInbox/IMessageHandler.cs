using MediatR;

namespace Messaging.OutboxInbox;

/// <summary>
/// Base interface for all message handlers.
/// Handlers implementing this interface will be invoked when messages are processed from the inbox.
/// </summary>
/// <typeparam name="TMessage">The type of message this handler processes</typeparam>
public interface IMessageHandler<in TMessage> : IRequestHandler<TMessage>where TMessage : IMessage
{

}
