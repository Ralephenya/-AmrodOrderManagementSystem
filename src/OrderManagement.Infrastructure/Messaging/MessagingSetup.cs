using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Infrastructure.Persistence;

namespace OrderManagement.Infrastructure.Messaging;

public static class MessagingSetup
{
    /// <summary>Aspire resource name, so the connection string arrives as <c>ConnectionStrings:messaging</c>.</summary>
    public const string ConnectionStringName = "messaging";

    /// <summary>
    /// MassTransit over RabbitMQ with the EF Core transactional outbox, shared by the API and the Worker.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Publishing</b> (<paramref name="publishThroughOutbox"/>): <c>Publish</c> writes to the <c>OutboxMessage</c>
    /// table in the same transaction as the business change. A background service delivers it to RabbitMQ after commit.
    /// No lost events when the broker is down, and no events for changes that rolled back (the dual-write problem).</item>
    /// <item><b>Consuming</b>: every endpoint gets exponential retry, then MassTransit moves the message to the
    /// <c>&lt;queue&gt;_error</c> queue (dead letter). The inbox records consumed <c>MessageId</c>s in the same transaction as
    /// the consumer's changes, so a redelivered message is acknowledged without running again (at-least-once delivery,
    /// effectively-once processing).</item>
    /// <item>Queues and exchanges are durable (the RabbitMQ transport default): messages survive a broker restart.</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool publishThroughOutbox,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        var options = configuration.GetSection(MessagingOptions.SectionName).Get<MessagingOptions>() ?? new MessagingOptions();
        var hostsConsumers = configureConsumers is not null;

        services.AddMassTransit(bus =>
        {
            bus.SetKebabCaseEndpointNameFormatter(); // OrderCreatedConsumer → "order-created" queue
            configureConsumers?.Invoke(bus);

            bus.AddEntityFrameworkOutbox<AppDbContext>(outbox =>
            {
                outbox.UseSqlServer();
                outbox.QueryDelay = options.OutboxQueryDelay;
                outbox.DuplicateDetectionWindow = options.DuplicateDetectionWindow;
                if (publishThroughOutbox)
                {
                    outbox.UseBusOutbox();
                }

                if (!hostsConsumers)
                {
                    // Publisher-only hosts (the API) never write InboxState, so they don't need to sweep it.
                    outbox.DisableInboxCleanupService();
                }
            });

            if (hostsConsumers)
            {
                bus.AddConfigureEndpointsCallback((context, _, endpoint) =>
                {
                    // Outermost first: each retry runs a fresh attempt through the inbox and the consumer.
                    endpoint.UseMessageRetry(retry => retry.Exponential(
                        options.Retry.Limit, options.Retry.MinInterval, options.Retry.MaxInterval, options.Retry.IntervalDelta));
                    endpoint.UseEntityFrameworkOutbox<AppDbContext>(context);
                    endpoint.UseConsumeFilter(typeof(CorrelationIdConsumeFilter<>), context);
                });
            }

            if (options.Transport == MessagingTransport.InMemory)
            {
                bus.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
            }
            else
            {
                bus.UsingRabbitMq((context, cfg) =>
                {
                    var connectionString = configuration.GetConnectionString(ConnectionStringName)
                        ?? throw new InvalidOperationException(
                            $"Connection string '{ConnectionStringName}' is missing. See docs/ONBOARDING.md.");
                    cfg.Host(new Uri(connectionString));
                    cfg.ConfigureEndpoints(context);
                });
            }
        });

        return services;
    }
}
