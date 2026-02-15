namespace Messaging.OutboxInbox.AspNetCore.Extensions;

public sealed class MessagingConfiguration
{
    internal Dictionary<Type, Type> MessageHandlers { get; } = new();

    public MessagingConfiguration AddSubscriber<TMessage, THandler>()
        where TMessage : IMessage
        where THandler : class, IMessageHandler<TMessage>
    {
        MessageHandlers[typeof(TMessage)] = typeof(THandler);
        return this;
    }
}