using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderManagement.Contracts.Orders;
using OrderManagement.IntegrationTests.Fixtures;
using OrderManagement.Worker;
using OrderManagement.Worker.Allocation;

namespace OrderManagement.IntegrationTests.Messaging;

/// <summary>
/// The worker's real registration (<see cref="WorkerSetup.AddWorker"/>: consumers, retry, inbox, filters) against the
/// test database, with MassTransit's in-memory test harness standing in for RabbitMQ.
/// </summary>
public sealed class WorkerHarness : IAsyncDisposable
{
    public const int RetryLimit = 2;

    private readonly ServiceProvider _provider;

    private WorkerHarness(ServiceProvider provider, CountingAllocator allocator)
    {
        _provider = provider;
        Allocator = allocator;
        Harness = provider.GetRequiredService<ITestHarness>();
    }

    public ITestHarness Harness { get; }

    public IServiceProvider Services => _provider;

    public CountingAllocator Allocator { get; }

    public static async Task<WorkerHarness> StartAsync(SqlServerDatabase database)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrdersDb"] = database.ConnectionString,
                ["Messaging:Transport"] = "InMemory",
                // Fast retries so dead-letter tests take milliseconds, not the production ~1 minute.
                ["Messaging:Retry:Limit"] = RetryLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Messaging:Retry:MinInterval"] = "00:00:00.010",
                ["Messaging:Retry:MaxInterval"] = "00:00:00.050",
                ["Messaging:Retry:IntervalDelta"] = "00:00:00.010",
            })
            .Build();

        var allocator = new CountingAllocator();
        var services = new ServiceCollection()
            .AddLogging()
            .AddMetrics()
            .AddSingleton<IConfiguration>(configuration);
        services.AddWorker(configuration);
        services.Replace(ServiceDescriptor.Singleton<IStockAllocator>(allocator));
        services.AddMassTransitTestHarness();

        var provider = services.BuildServiceProvider(validateScopes: true);
        var worker = new WorkerHarness(provider, allocator);
        await worker.Harness.Start();
        return worker;
    }

    /// <summary>Publishes as the API would, optionally with a fixed message ID (to simulate broker redelivery).</summary>
    public Task Publish<T>(T message, Guid? messageId = null)
        where T : class =>
        Harness.Bus.Publish(message, context =>
        {
            if (messageId is { } id)
            {
                context.MessageId = id;
            }
        });

    public async ValueTask DisposeAsync()
    {
        await Harness.Stop();
        await _provider.DisposeAsync();
    }

    /// <summary>Records allocations instead of waiting on a warehouse, and can simulate a failing warehouse.</summary>
    public sealed class CountingAllocator : IStockAllocator
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> _calls = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> _failing = new();

        public int Calls => _calls.Values.Sum();

        public int CallsFor(Guid orderId) => _calls.GetValueOrDefault(orderId);

        /// <summary>Every allocation for <paramref name="orderId"/> throws, like a warehouse that is down.</summary>
        public void FailFor(Guid orderId) => _failing[orderId] = true;

        public Task AllocateAsync(Guid orderId, IReadOnlyList<OrderCreatedLine> lines, CancellationToken ct)
        {
            _calls.AddOrUpdate(orderId, 1, (_, n) => n + 1);
            return _failing.ContainsKey(orderId)
                ? throw new InvalidOperationException("Warehouse unavailable (simulated).")
                : Task.CompletedTask;
        }
    }
}
