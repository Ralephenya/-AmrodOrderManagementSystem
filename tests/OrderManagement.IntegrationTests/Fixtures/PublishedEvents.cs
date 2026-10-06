using System.Collections.Concurrent;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Contracts;
using OrderManagement.Contracts.Orders;

namespace OrderManagement.IntegrationTests.Fixtures;

/// <summary>
/// A receive endpoint connected to the API's own bus that records every order event it is delivered. Only messages the
/// outbox delivery service actually sends (after commit) can arrive here, so this observes real delivery, not staging.
/// </summary>
public sealed class PublishedEvents : IAsyncDisposable
{
    private readonly ConcurrentQueue<Delivered> _delivered = new();
    private HostReceiveEndpointHandle? _handle;

    public static async Task<PublishedEvents> ConnectAsync(IServiceProvider services)
    {
        var recorder = new PublishedEvents();
        var bus = services.GetRequiredService<IBus>();
        recorder._handle = bus.ConnectReceiveEndpoint("integration-tests-recorder", endpoint =>
        {
            endpoint.Handler<OrderCreated>(context => recorder.Record(context));
            endpoint.Handler<OrderPaid>(context => recorder.Record(context));
        });
        await recorder._handle.Ready;
        return recorder;
    }

    public IReadOnlyList<Delivered> All => [.. _delivered];

    public IEnumerable<T> Of<T>() => _delivered.Select(d => d.Message).OfType<T>();

    /// <summary>Waits for a delivered message of <typeparamref name="T"/> that satisfies <paramref name="match"/>.</summary>
    public async Task<Delivered> WaitForAsync<T>(Func<T, bool> match, TimeSpan? timeout = null)
    {
        Delivered? found = null;
        await Eventually.UntilAsync(
            () => (found = _delivered.FirstOrDefault(d => d.Message is T message && match(message))) is not null,
            $"a delivered {typeof(T).Name}",
            timeout);
        return found!;
    }

    public async ValueTask DisposeAsync()
    {
        if (_handle is not null)
        {
            await _handle.StopAsync();
        }
    }

    private Task Record<T>(ConsumeContext<T> context)
        where T : class
    {
        _delivered.Enqueue(new Delivered(context.Message, context.Headers.Get<string>(MessageHeaderNames.CorrelationId)));
        return Task.CompletedTask;
    }

    public sealed record Delivered(object Message, string? CorrelationId);
}
