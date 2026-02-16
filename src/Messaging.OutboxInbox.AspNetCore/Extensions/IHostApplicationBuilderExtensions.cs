using Messaging.OutboxInbox.AspNetCore.Extensions.DbContextExtensions;
using Messaging.OutboxInbox.AspNetCore.HostedServices;
using Messaging.OutboxInbox.AspNetCore.MessageBroker;
using Messaging.OutboxInbox.AspNetCore.Options;
using Messaging.OutboxInbox.AspNetCore.Queues;
using Messaging.OutboxInbox.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

namespace Messaging.OutboxInbox.AspNetCore.Extensions;

public static class IHostApplicationBuilderExtensions
{
    public static IHostApplicationBuilder AddOutboxMessaging<TContext>(this IHostApplicationBuilder builder)
        where TContext : DbContext
    {
        // Configure Options
        builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.Section));
        builder.Services.Configure<MessagePublisherOptions>(builder.Configuration.GetSection(MessagePublisherOptions.Section));

        // RabbitMQ Connection - Only if not registered by Aspire
        builder.Services.TryAddSingleton<IConnection>(sp =>
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

        // THE MAGIC: Register the interceptor (Singleton because it's stateless except for queue injection)
        builder.Services.AddSingleton<OutboxEnqueueInterceptor>();

        // Core Services (Scoped)
        builder.Services.AddScoped<IOutboxMessagesService, OutboxMessagesService>();
        builder.Services.AddScoped<RabbitMqPublisher>();
        builder.Services.AddScoped<IMessagePublisher, MessagePublisher>();

        // Map base types
        builder.Services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());

        // Hosted Service
        builder.Services.AddHostedService<OutboxHostedService>();

        return builder;
    }

    public static IHostApplicationBuilder AddInboxMessaging<TContext>(this IHostApplicationBuilder builder)
        where TContext : DbContext
    {
        // Configure Options
        builder.Services.TryConfigureOptions<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.Section));
        builder.Services.Configure<MessageSubscriberOptions>(builder.Configuration.GetSection(MessageSubscriberOptions.Section));

        // RabbitMQ Connection
        builder.Services.TryAddSingleton<IConnection>(sp =>
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
        builder.Services.AddSingleton<IInboxMessageQueue, InboxMessageQueue>();

        // Core Services (Scoped)
        builder.Services.TryAddScoped<DbContext, TContext>();
        builder.Services.AddScoped<IInboxMessagesService, InboxMessagesService>();

        // MediatR
        builder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TContext).Assembly));

        // Hosted Services
        builder.Services.AddHostedService<InboxHostedService>();
        builder.Services.AddHostedService<RabbitMqSubscriber>();

        return builder;
    }

    public static IHostApplicationBuilder AddMessagingHandlers<TContext>(
        this IHostApplicationBuilder builder,
        Action<MessagingConfiguration>? configure = null)
        where TContext : DbContext
    {
        var config = new MessagingConfiguration();
        configure?.Invoke(config);

        // Configure shared options
        builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.Section));
        builder.Services.Configure<MessagePublisherOptions>(builder.Configuration.GetSection(MessagePublisherOptions.Section));
        builder.Services.Configure<MessageSubscriberOptions>(builder.Configuration.GetSection(MessageSubscriberOptions.Section));

        // RabbitMQ Connection
        builder.Services.TryAddSingleton<IConnection>(sp =>
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

        // Queues (Singleton)
        builder.Services.AddSingleton<IOutboxMessageQueue, OutboxMessageQueue>();
        builder.Services.AddSingleton<IInboxMessageQueue, InboxMessageQueue>();

        // THE MAGIC: Register the interceptor
        builder.Services.AddSingleton<OutboxEnqueueInterceptor>();

        // Map base types
        builder.Services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());

        // Core Services (Scoped)
        builder.Services.AddScoped<IOutboxMessagesService, OutboxMessagesService>();
        builder.Services.AddScoped<IInboxMessagesService, InboxMessagesService>();
        builder.Services.AddScoped<IMessagePublisher, MessagePublisher>();
        builder.Services.AddScoped<RabbitMqPublisher>();

        // MediatR with assembly scanning
        builder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TContext).Assembly));

        // Hosted Services
        builder.Services.AddHostedService<OutboxHostedService>();
        builder.Services.AddHostedService<InboxHostedService>();
        builder.Services.AddHostedService<RabbitMqSubscriber>();

        // Register message handlers - auto-discover
        builder.Services.Scan(scan => scan
            .FromAssembliesOf(typeof(TContext))
            .AddClasses(classes => classes.AssignableTo(typeof(IMessageHandler<>)))
            .AsSelfWithInterfaces()
            .WithScopedLifetime());

        // Also register explicitly configured handlers (different assemblies)
        foreach (var (_, handlerType) in config.MessageHandlers)
        {
            builder.Services.TryAddScoped(handlerType);
        }

        return builder;
    }

    private static IServiceCollection TryConfigureOptions<TOptions>(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfigurationSection section)
        where TOptions : class
    {
        if (!services.Any(x => x.ServiceType == typeof(Microsoft.Extensions.Options.IOptions<TOptions>)))
        {
            services.Configure<TOptions>(section);
        }
        return services;
    }
}