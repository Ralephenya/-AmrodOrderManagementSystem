using OrderManagement.Infrastructure;
using OrderManagement.Infrastructure.Messaging;
using OrderManagement.Worker.Allocation;
using OrderManagement.Worker.Consumers;

namespace OrderManagement.Worker;

/// <summary>Everything the worker needs, in one place, so Program.cs and the tests configure exactly the same thing.</summary>
public static class WorkerSetup
{
    public static IServiceCollection AddWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddInfrastructure();

        services.Configure<AllocationOptions>(configuration.GetSection(AllocationOptions.SectionName));
        services.AddSingleton<IStockAllocator, SimulatedStockAllocator>();

        services.AddMessaging(configuration, publishThroughOutbox: false, bus =>
        {
            bus.AddConsumer<OrderCreatedConsumer>();
            bus.AddConsumer<OrderPaidConsumer>();
        });

        return services;
    }
}
