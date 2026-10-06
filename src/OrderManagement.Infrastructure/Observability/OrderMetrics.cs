using System.Diagnostics.Metrics;
using OrderManagement.Domain.Orders;

namespace OrderManagement.Infrastructure.Observability;

/// <summary>
/// Business metrics for the order pipeline, exported through OpenTelemetry (Aspire dashboard, or any OTLP backend).
/// HTTP request duration and counts come from the ASP.NET Core instrumentation in ServiceDefaults. These counters
/// add the "is the business flowing?" view: are orders being created, allocated and fulfilled?
/// </summary>
public sealed class OrderMetrics
{
    /// <summary>ServiceDefaults subscribes to <c>OrderManagement.*</c>.</summary>
    public const string MeterName = "OrderManagement.Orders";

    private readonly Counter<long> _created;
    private readonly Counter<long> _statusChanged;
    private readonly Counter<long> _allocated;
    private readonly Counter<long> _fulfilled;

    public OrderMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);

        _created = meter.CreateCounter<long>("orders.created", "{order}", "Orders created");
        _statusChanged = meter.CreateCounter<long>("orders.status_changes", "{change}", "Order status transitions applied");
        _allocated = meter.CreateCounter<long>("orders.allocated", "{order}", "Orders whose stock was allocated");
        _fulfilled = meter.CreateCounter<long>("orders.fulfilled", "{order}", "Orders fulfilled");
    }

    /// <summary>Tagged by currency. Amounts aren't summed into one metric, because currencies can't be added together.</summary>
    public void OrderCreated(string currencyCode) =>
        _created.Add(1, new KeyValuePair<string, object?>("currency", currencyCode));

    public void StatusChanged(OrderStatus from, OrderStatus to)
    {
        _statusChanged.Add(1,
            new KeyValuePair<string, object?>("from", from.ToString()),
            new KeyValuePair<string, object?>("to", to.ToString()));

        if (to == OrderStatus.Fulfilled)
        {
            OrderFulfilled("api");
        }
    }

    public void OrderAllocated() => _allocated.Add(1);

    /// <param name="source"><c>api</c> (manual transition) or <c>worker</c> (automatic after allocation or payment).</param>
    public void OrderFulfilled(string source) =>
        _fulfilled.Add(1, new KeyValuePair<string, object?>("source", source));
}
