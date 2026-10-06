using Microsoft.Extensions.Options;
using OrderManagement.Contracts.Orders;

namespace OrderManagement.Worker.Allocation;

/// <summary>Reserves stock for an order's lines. In a real system this would call the warehouse (WMS) or ERP.</summary>
/// <remarks>
/// <b>Implementations must be idempotent per <c>orderId</c></b>: send the order ID as the reservation key, so calling
/// twice holds one reservation, not two. The consumer calls this before saving, so a retry (for example after the API
/// changed the order mid-allocation and the save failed on rowversion) calls it again for the same order.
/// </remarks>
public interface IStockAllocator
{
    Task AllocateAsync(Guid orderId, IReadOnlyList<OrderCreatedLine> lines, CancellationToken ct);
}

public sealed class AllocationOptions
{
    public const string SectionName = "Allocation";

    /// <summary>Simulated warehouse latency, so the Pending → allocated → Fulfilled flow is visible in the UI.</summary>
    public TimeSpan SimulatedDelay { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>Stands in for the warehouse integration: waits, then reports success.</summary>
public sealed partial class SimulatedStockAllocator(IOptions<AllocationOptions> options, ILogger<SimulatedStockAllocator> logger)
    : IStockAllocator
{
    public async Task AllocateAsync(Guid orderId, IReadOnlyList<OrderCreatedLine> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lines);

        LogAllocating(logger, orderId, lines.Count, lines.Sum(l => l.Quantity));
        await Task.Delay(options.Value.SimulatedDelay, ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Allocating stock for order {OrderId}: {LineCount} lines, {Units} units")]
    private static partial void LogAllocating(ILogger logger, Guid orderId, int lineCount, int units);
}
