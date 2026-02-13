// src/Messaging.OutboxInbox.AspNetCore/Configuration/MessagingConfiguration.cs
using Messaging.OutboxInbox.Abstractions;

namespace Messaging.OutboxInbox.AspNetCore.Configuration;

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