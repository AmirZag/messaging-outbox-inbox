using Messaging.OutboxInbox.Abstractions;
using Messaging.OutboxInbox.AspNetCore.Configuration;
using Messaging.OutboxInbox.AspNetCore.HostedServices;
using Messaging.OutboxInbox.AspNetCore.MessageBroker;
using Messaging.OutboxInbox.AspNetCore.Options;
using Messaging.OutboxInbox.AspNetCore.Queues;
using Messaging.OutboxInbox.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

namespace Messaging.OutboxInbox.AspNetCore.Extensions;

public static class OutboxInboxServiceExtensions
{
    public static IHostApplicationBuilder AddOutboxMessaging<TContext>(this IHostApplicationBuilder builder)
        where TContext : OutboxInboxContext
    {
        // Configure Options
        builder.Services.Configure<RabbitMqOptions>(
            builder.Configuration.GetSection(RabbitMqOptions.Section));
        builder.Services.Configure<MessagePublisherOptions>(
            builder.Configuration.GetSection(MessagePublisherOptions.Section));

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

        // Queue (Singleton) - MUST be registered before DbContext
        builder.Services.AddSingleton<IOutboxMessageQueue, OutboxMessageQueue>();

        // DbContext Factory with queue hookup
        builder.Services.AddDbContextFactory<TContext>();

        // Core Services (Scoped)
        builder.Services.AddScoped<IOutboxMessagesService, OutboxMessagesService>();
        builder.Services.AddScoped<IMessagePublisher, MessagePublisher>();
        builder.Services.AddScoped<RabbitMqPublisher>();

        // Configure DbContext with queue hookup - THIS IS THE KEY FIX
        builder.Services.AddScoped<TContext>(sp =>
        {
            var contextFactory = sp.GetRequiredService<IDbContextFactory<TContext>>();
            var context = contextFactory.CreateDbContext();
            var queue = sp.GetRequiredService<IOutboxMessageQueue>();

            // Hook up the queue to the context's SaveChanges
            context.SetOutboxEnqueueAction(queue.Enqueue);

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

        // RabbitMQ Connection (Singleton) - only if not already registered
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

        // DbContext Factory - only if not already registered
        if (!builder.Services.Any(x => x.ServiceType == typeof(IDbContextFactory<TContext>)))
        {
            builder.Services.AddDbContextFactory<TContext>();
        }

        // Core Services (Scoped)
        if (!builder.Services.Any(x => x.ServiceType == typeof(DbContext)))
        {
            builder.Services.AddScoped<DbContext, TContext>();
        }

        builder.Services.AddScoped<IInboxMessagesService, InboxMessagesService>();

        // MediatR - register handlers from the assembly containing TContext
        builder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TContext).Assembly));

        // Hosted Services - RabbitMqSubscriber is now directly a hosted service
        builder.Services.AddHostedService<InboxHostedService>();
        builder.Services.AddHostedService<RabbitMqSubscriber>();

        return builder;
    }

    public static IHostApplicationBuilder AddOutboxAndInboxMessaging<TContext>(
        this IHostApplicationBuilder builder,
        Action<MessagingConfiguration>? configure = null)
        where TContext : OutboxInboxContext
    {
        var config = new MessagingConfiguration();
        configure?.Invoke(config);

        // Configure shared RabbitMQ connection first
        builder.Services.Configure<RabbitMqOptions>(
            builder.Configuration.GetSection(RabbitMqOptions.Section));

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

        // Add outbox (without adding RabbitMQ connection again)
        builder.Services.Configure<MessagePublisherOptions>(
            builder.Configuration.GetSection(MessagePublisherOptions.Section));
        builder.Services.AddSingleton<IOutboxMessageQueue, OutboxMessageQueue>();
        builder.Services.AddDbContextFactory<TContext>();
        builder.Services.AddScoped<IOutboxMessagesService, OutboxMessagesService>();
        builder.Services.AddScoped<IMessagePublisher, MessagePublisher>();
        builder.Services.AddScoped<RabbitMqPublisher>();

        builder.Services.AddScoped<TContext>(sp =>
        {
            var contextFactory = sp.GetRequiredService<IDbContextFactory<TContext>>();
            var context = contextFactory.CreateDbContext();
            var queue = sp.GetRequiredService<IOutboxMessageQueue>();
            context.SetOutboxEnqueueAction(queue.Enqueue);
            return context;
        });

        builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        builder.Services.AddHostedService<OutboxHostedService>();

        // Add inbox
        builder.Services.Configure<MessageSubscriberOptions>(
            builder.Configuration.GetSection(MessageSubscriberOptions.Section));
        builder.Services.AddSingleton<IInboxMessageQueue, InboxMessageQueue>();
        builder.Services.AddScoped<IInboxMessagesService, InboxMessagesService>();
        builder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TContext).Assembly));
        builder.Services.AddHostedService<InboxHostedService>();
        builder.Services.AddHostedService<RabbitMqSubscriber>();

        // Register message handlers
        foreach (var (_, handlerType) in config.MessageHandlers)
        {
            builder.Services.AddScoped(handlerType);
        }

        return builder;
    }
}