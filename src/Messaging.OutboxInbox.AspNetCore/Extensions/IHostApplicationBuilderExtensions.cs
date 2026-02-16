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
    extension(IHostApplicationBuilder builder)
    {
        public IHostApplicationBuilder AddOutboxMessaging<TContext>()
            where TContext : DbContext
        {
            builder.ConfigureOutboxOptions();
            builder.AddRabbitMqConnection();
            builder.AddOutboxQueue();
            builder.AddOutboxInterceptor();
            builder.AddOutboxServices<TContext>();
            builder.AddOutboxHostedService();

            return builder;
        }

        public IHostApplicationBuilder AddInboxMessaging<TContext>()
            where TContext : DbContext
        {
            builder.ConfigureInboxOptions();
            builder.AddRabbitMqConnection();
            builder.AddInboxQueue();
            builder.AddInboxServices<TContext>();
            builder.AddMediatRForInbox<TContext>();
            builder.AddInboxHostedServices();

            return builder;
        }

        public IHostApplicationBuilder AddMessagingHandlers<TContext>(Action<MessagingConfiguration>? configure = null)
            where TContext : DbContext
        {
            var config = new MessagingConfiguration();
            configure?.Invoke(config);

            builder.ConfigureAllMessagingOptions();
            builder.AddRabbitMqConnection();
            builder.AddMessagingQueues();
            builder.AddOutboxInterceptor();
            builder.AddAllMessagingServices<TContext>();
            builder.AddMediatRWithBehaviors<TContext>();
            builder.AddAllHostedServices();
            builder.ScanAndRegisterHandlers<TContext>(config);

            return builder;
        }

        // Private composition methods
        private void ConfigureOutboxOptions()
        {
            builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.Section));
            builder.Services.Configure<MessagePublisherOptions>(builder.Configuration.GetSection(MessagePublisherOptions.Section));
        }

        private void ConfigureInboxOptions()
        {
            builder.Services.TryConfigureOptions<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.Section));
            builder.Services.Configure<MessageSubscriberOptions>(builder.Configuration.GetSection(MessageSubscriberOptions.Section));
        }

        private void ConfigureAllMessagingOptions()
        {
            builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.Section));
            builder.Services.Configure<MessagePublisherOptions>(builder.Configuration.GetSection(MessagePublisherOptions.Section));
            builder.Services.Configure<MessageSubscriberOptions>(builder.Configuration.GetSection(MessageSubscriberOptions.Section));
        }

        private void AddRabbitMqConnection()
        {
            builder.Services.TryAddSingleton<IConnection>(sp =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value;
                
                if (string.IsNullOrEmpty(options.HostName))
                    throw new InvalidOperationException("RabbitMQ:HostName is required in configuration");

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

        private void AddOutboxQueue()
        {
            builder.Services.AddSingleton<IOutboxMessageQueue, OutboxMessageQueue>();
        }

        private void AddInboxQueue()
        {
            builder.Services.AddSingleton<IInboxMessageQueue, InboxMessageQueue>();
        }

        private void AddMessagingQueues()
        {
            builder.Services.AddSingleton<IOutboxMessageQueue, OutboxMessageQueue>();
            builder.Services.AddSingleton<IInboxMessageQueue, InboxMessageQueue>();
        }

        private void AddOutboxInterceptor()
        {
            builder.Services.AddSingleton<OutboxEnqueueInterceptor>();
        }

        private void AddOutboxServices<TContext>()
            where TContext : DbContext
        {
            builder.Services.AddScoped<IOutboxMessagesService, OutboxMessagesService>();
            builder.Services.AddScoped<RabbitMqPublisher>();
            builder.Services.AddScoped<IMessagePublisher, MessagePublisher>();
            builder.Services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        }

        private void AddInboxServices<TContext>()
            where TContext : DbContext
        {
            builder.Services.TryAddScoped<DbContext, TContext>();
            builder.Services.AddScoped<IInboxMessagesService, InboxMessagesService>();
        }

        private void AddAllMessagingServices<TContext>()
            where TContext : DbContext
        {
            builder.Services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
            builder.Services.AddScoped<IOutboxMessagesService, OutboxMessagesService>();
            builder.Services.AddScoped<IInboxMessagesService, InboxMessagesService>();
            builder.Services.AddScoped<IMessagePublisher, MessagePublisher>();
            builder.Services.AddScoped<RabbitMqPublisher>();
        }

        private void AddMediatRForInbox<TContext>()
            where TContext : DbContext
        {
            builder.Services.AddMediatR(cfg =>
                cfg.RegisterServicesFromAssembly(typeof(TContext).Assembly));
        }

        private void AddMediatRWithBehaviors<TContext>()
            where TContext : DbContext
        {
            builder.Services.AddMediatR(cfg =>
                cfg.RegisterServicesFromAssembly(typeof(TContext).Assembly));
        }

        private void AddOutboxHostedService()
        {
            builder.Services.AddHostedService<OutboxHostedService>();
        }

        private void AddInboxHostedServices()
        {
            builder.Services.AddHostedService<InboxHostedService>();
            builder.Services.AddHostedService<RabbitMqSubscriber>();
        }

        private void AddAllHostedServices()
        {
            builder.Services.AddHostedService<OutboxHostedService>();
            builder.Services.AddHostedService<InboxHostedService>();
            builder.Services.AddHostedService<RabbitMqSubscriber>();
        }

        private void ScanAndRegisterHandlers<TContext>(MessagingConfiguration config)
            where TContext : DbContext
        {
            builder.Services.Scan(scan => scan
                .FromAssembliesOf(typeof(TContext))
                .AddClasses(classes => classes.AssignableTo(typeof(IMessageHandler<>)))
                .AsSelfWithInterfaces()
                .WithScopedLifetime());

            foreach (var (_, handlerType) in config.MessageHandlers)
            {
                builder.Services.TryAddScoped(handlerType);
            }
        }
    }

    extension(IServiceCollection services)
    {
        internal IServiceCollection TryConfigureOptions<TOptions>(
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
}