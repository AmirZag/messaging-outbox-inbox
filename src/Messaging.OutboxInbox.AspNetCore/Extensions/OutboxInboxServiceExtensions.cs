using Messaging.OutboxInbox.Abstractions;
using Messaging.OutboxInbox.AspNetCore.Configuration;
using Messaging.OutboxInbox.AspNetCore.HostedServices;
using Messaging.OutboxInbox.AspNetCore.MessageBroker;
using Messaging.OutboxInbox.AspNetCore.Options;
using Messaging.OutboxInbox.AspNetCore.Publishers;
using Messaging.OutboxInbox.AspNetCore.Queues;
using Messaging.OutboxInbox.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

namespace Messaging.OutboxInbox.AspNetCore.Extensions;

public static class OutboxInboxExtensions
{
    public static IHostApplicationBuilder AddOutboxMessaging<TContext>(this IHostApplicationBuilder builder)
        where TContext : OutboxInboxContext
    {
        // Configure Options
        builder.Services.Configure<RabbitMqOptions>(
            builder.Configuration.GetSection(RabbitMqOptions.Section));
        builder.Services.Configure<MessagePublisherOptions>(
            builder.Configuration.GetSection(MessagePublisherOptions.Section));

        // DbContext Factory
        builder.Services.AddDbContextFactory<TContext>();

        // RabbitMQ Connection (Singleton)
        builder.Services.AddSingleton<IConnection>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value;
            var factory = new ConnectionFactory
            {
                HostName = options.HostName,
                Port = options.Port,
                UserName = options.UserName,
                Password = options.Password
            };

            return factory.CreateConnectionAsync().GetAwaiter().GetResult();
        });

        // Queue (Singleton)
        builder.Services.AddSingleton<IOutboxMessageQueue, OutboxMessageQueue>();

        // Core Services (Scoped)
        builder.Services.AddScoped<IOutboxMessagesService, OutboxMessagesService>();
        builder.Services.AddScoped<IMessagePublisher, MessagePublisher>();

        // RabbitMQ Publisher (Scoped)
        builder.Services.AddScoped<RabbitMqPublisher>();

        // Configure DbContext with direct queue enqueue
        builder.Services.AddScoped<TContext>(sp =>
        {
            var contextFactory = sp.GetRequiredService<IDbContextFactory<TContext>>();
            var context = contextFactory.CreateDbContext();
            var queue = sp.GetRequiredService<IOutboxMessageQueue>();

            // Directly enqueue messages after successful save
            context.SetOutboxEnqueue(message => queue.Enqueue(message));

            return context;
        });

        // Also register as DbContext for services that need it
        builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());

        // Hosted Service
        builder.Services.AddHostedService<OutboxHostedService>();

        return builder;
    }

    public static IHostApplicationBuilder AddInboxMessaging<TContext>(this IHostApplicationBuilder builder)
        where TContext : OutboxInboxContext
    {
        // Configure Options
        if (!builder.Services.Any(x => x.ServiceType == typeof(Microsoft.Extensions.Options.IOptions<RabbitMqOptions>)))
        {
            builder.Services.Configure<RabbitMqOptions>(
                builder.Configuration.GetSection(RabbitMqOptions.Section));
        }

        builder.Services.Configure<MessageSubscriberOptions>(
            builder.Configuration.GetSection(MessageSubscriberOptions.Section));

        // DbContext Factory
        if (!builder.Services.Any(x => x.ServiceType == typeof(IDbContextFactory<TContext>)))
        {
            builder.Services.AddDbContextFactory<TContext>();
        }

        // RabbitMQ Connection (Singleton)
        if (!builder.Services.Any(x => x.ServiceType == typeof(IConnection)))
        {
            builder.Services.AddSingleton<IConnection>(sp =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value;
                var factory = new ConnectionFactory
                {
                    HostName = options.HostName,
                    Port = options.Port,
                    UserName = options.UserName,
                    Password = options.Password
                };

                return factory.CreateConnectionAsync().GetAwaiter().GetResult();
            });
        }

        // Queue (Singleton)
        builder.Services.AddSingleton<IInboxMessageQueue, InboxMessageQueue>();

        // Core Services (Scoped)
        if (!builder.Services.Any(x => x.ServiceType == typeof(DbContext)))
        {
            builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        }

        builder.Services.AddScoped<IInboxMessagesService, InboxMessagesService>();

        // RabbitMQ Subscriber (Singleton)
        builder.Services.AddSingleton<RabbitMqSubscriber>();

        // MediatR
        builder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TContext).Assembly));

        // Hosted Services
        builder.Services.AddHostedService<InboxHostedService>();
        builder.Services.AddHostedService<InboxSubscriberService>();

        return builder;
    }

    public static IHostApplicationBuilder AddOutboxAndInboxMessaging<TContext>(
        this IHostApplicationBuilder builder,
        Action<MessagingConfiguration>? configure = null)
        where TContext : OutboxInboxContext
    {
        var config = new MessagingConfiguration();
        configure?.Invoke(config);

        // Add both outbox and inbox
        builder.AddOutboxMessaging<TContext>();
        builder.AddInboxMessaging<TContext>();

        // Register message handlers
        foreach (var (_, handlerType) in config.MessageHandlers)
        {
            builder.Services.AddScoped(handlerType);
        }

        return builder;
    }
}