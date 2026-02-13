// src/Messaging.OutboxInbox.AspNetCore/Extensions/OutboxInboxServiceExtensions.cs
using MediatR;
using Messaging.OutboxInbox;
using Messaging.OutboxInbox.Abstractions;
using Messaging.OutboxInbox.AspNetCore.BackgroundServices;
using Messaging.OutboxInbox.AspNetCore.Configuration;
using Messaging.OutboxInbox.AspNetCore.Options;
using Messaging.OutboxInbox.AspNetCore.Publishers;
using Messaging.OutboxInbox.AspNetCore.Retry;
using Messaging.OutboxInbox.AspNetCore.Signals;
using Messaging.OutboxInbox.AspNetCore.Subscribers;
using Messaging.OutboxInbox.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

namespace Messaging.OutboxInbox.AspNetCore.Extensions;

public static class OutboxInboxServiceExtensions
{
    public static IHostApplicationBuilder AddOutboxMessaging<TContext>(this IHostApplicationBuilder builder)
        where TContext : OutboxInboxContext
    {
        // Register options
        builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
        builder.Services.Configure<MessagePublisherOptions>(builder.Configuration.GetSection(MessagePublisherOptions.SectionName));
        builder.Services.Configure<OutboxProcessorOptions>(builder.Configuration.GetSection(OutboxProcessorOptions.SectionName));

        // Register DbContextFactory
        builder.Services.AddDbContextFactory<TContext>();

        // Register RabbitMQ connection
        builder.Services.AddSingleton<IConnection>(sp =>
        {
            var rabbitOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value;
            var factory = new ConnectionFactory
            {
                HostName = rabbitOptions.HostName,
                Port = rabbitOptions.Port,
                UserName = rabbitOptions.UserName,
                Password = rabbitOptions.Password
            };
            return factory.CreateConnectionAsync().GetAwaiter().GetResult();
        });

        // Register services
        builder.Services.AddScoped<IMessagePublisher, OutboxMessagePublisher>();
        builder.Services.AddScoped<IOutboxMessagesService, OutboxMessagesService>();
        builder.Services.AddSingleton<IOutboxSignal, OutboxSignal>();
        builder.Services.AddSingleton<IRetryTracker, RetryTracker>();
        builder.Services.AddScoped<RabbitMqPublisher>();

        // Register background service
        builder.Services.AddHostedService<OutboxProcessorService>();

        return builder;
    }

    public static IHostApplicationBuilder AddInboxMessaging<TContext>(this IHostApplicationBuilder builder)
        where TContext : OutboxInboxContext
    {
        // Register options
        if (!builder.Services.Any(x => x.ServiceType == typeof(Microsoft.Extensions.Options.IOptions<RabbitMqOptions>)))
        {
            builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
        }
        builder.Services.Configure<MessageSubscriberOptions>(builder.Configuration.GetSection(MessageSubscriberOptions.SectionName));
        builder.Services.Configure<InboxProcessorOptions>(builder.Configuration.GetSection(InboxProcessorOptions.SectionName));

        // Register DbContextFactory
        if (!builder.Services.Any(x => x.ServiceType == typeof(IDbContextFactory<OutboxInboxContext>)))
        {
            builder.Services.AddDbContextFactory<TContext>();
        }

        // Register RabbitMQ connection
        if (!builder.Services.Any(x => x.ServiceType == typeof(IConnection)))
        {
            builder.Services.AddSingleton<IConnection>(sp =>
            {
                var rabbitOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value;
                var factory = new ConnectionFactory
                {
                    HostName = rabbitOptions.HostName,
                    Port = rabbitOptions.Port,
                    UserName = rabbitOptions.UserName,
                    Password = rabbitOptions.Password
                };
                return factory.CreateConnectionAsync().GetAwaiter().GetResult();
            });
        }

        // Register services
        builder.Services.AddScoped<IInboxMessagesService, InboxMessagesService>();
        builder.Services.AddSingleton<IInboxSignal, InboxSignal>();

        if (!builder.Services.Any(x => x.ServiceType == typeof(IRetryTracker)))
        {
            builder.Services.AddSingleton<IRetryTracker, RetryTracker>();
        }

        builder.Services.AddSingleton<RabbitMqSubscriber>();

        // Register MediatR
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(TContext).Assembly));

        // Register background services
        builder.Services.AddHostedService<InboxProcessorService>();
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

        builder.AddOutboxMessaging<TContext>();
        builder.AddInboxMessaging<TContext>();

        foreach (var (_, handlerType) in config.MessageHandlers)
        {
            builder.Services.AddScoped(handlerType);
        }

        return builder;
    }
}