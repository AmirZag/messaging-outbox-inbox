using Messaging.OutboxInbox.AspNetCore.Extensions.DbContextExtensions;
using Messaging.OutboxInbox.AspNetCore.HostedServices;
using Messaging.OutboxInbox.AspNetCore.MessageBroker;
using Messaging.OutboxInbox.AspNetCore.Options;
using Messaging.OutboxInbox.AspNetCore.Queues;
using Messaging.OutboxInbox.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

            // Detect what's enabled in DbContext
            var (hasOutbox, hasInbox) = builder.DetectEnabledMessagingFeatures<TContext>();

            if (!hasOutbox && !hasInbox)
            {
                throw new InvalidOperationException(
                    "No messaging features enabled. Please add '.IncludeOutboxMessaging()' and/or '.IncludeInboxMessaging()' " +
                    "in your DbContext configuration.");
            }

            // Always configure RabbitMQ if any feature is enabled
            builder.AddRabbitMqConnection();

            if (hasOutbox)
            {
                builder.ConfigureOutboxOptions();
                builder.AddOutboxQueue();
                builder.AddOutboxInterceptor();
                builder.AddOutboxServices<TContext>();
                builder.AddOutboxHostedService();
            }

            if (hasInbox)
            {
                builder.ConfigureInboxOptions();
                builder.AddInboxQueue();
                builder.AddInboxServices<TContext>();
                builder.AddMediatRForInbox<TContext>();
                builder.AddInboxHostedServices();

                // Only scan/register handlers if inbox is enabled
                builder.ScanAndRegisterHandlers<TContext>(config);
            }
            else
            {
                // Inbox not enabled but user provided handler config - log warning
                if (config.MessageHandlers.Any())
                {
                    builder.LogConfigurationWarning(
                        "Handler configuration provided but Inbox messaging is not enabled. " +
                        "Handlers will be ignored. Add '.IncludeInboxMessaging()' to enable handler processing.");
                }
            }

            return builder;
        }

        // Feature detection
        private (bool hasOutbox, bool hasInbox) DetectEnabledMessagingFeatures<TContext>()
            where TContext : DbContext
        {
            // Get a temporary DbContext instance to check its configuration
            using var scope = builder.Services.BuildServiceProvider().CreateScope();

            try
            {
                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                var options = context.GetService<IDbContextOptions>();

                var hasOutbox = options.FindExtension<OutboxMessageOnlySupportOption>() != null;
                var hasInbox = options.FindExtension<InboxMessageOnlySupportOption>() != null;

                return (hasOutbox, hasInbox);
            }
            catch
            {
                // If we can't detect, assume both are enabled (backward compatibility)
                return (true, true);
            }
        }

        private void LogConfigurationWarning(string message)
        {
            // Log warning during startup - user will see it in console
            var sp = builder.Services.BuildServiceProvider();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger("Messaging.OutboxInbox.Configuration");
            logger?.LogWarning("CONFIGURATION WARNING: {Message}", message);
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

        private void AddMediatRForInbox<TContext>()
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