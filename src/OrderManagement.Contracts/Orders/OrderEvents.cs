namespace OrderManagement.Contracts.Orders;

// Integration events: the public, versioned contract between services. They are deliberately separate from the
// domain entities, so the schema can evolve without breaking consumers (add optional fields only; never rename or
// remove). MassTransit routes by type name, so these types are never moved or renamed either.

/// <summary>Published (via the transactional outbox) when an order is created. The worker allocates stock for it.</summary>
/// <param name="OrderId">The new order.</param>
/// <param name="CustomerId">Its customer.</param>
/// <param name="CurrencyCode">ISO 4217.</param>
/// <param name="TotalAmount">Server-computed total in <paramref name="CurrencyCode"/>.</param>
/// <param name="CreatedAt">UTC.</param>
/// <param name="Lines">What to allocate.</param>
public sealed record OrderCreated(
    Guid OrderId,
    Guid CustomerId,
    string CurrencyCode,
    decimal TotalAmount,
    DateTime CreatedAt,
    IReadOnlyList<OrderCreatedLine> Lines);

/// <param name="ProductSku">Upper-case SKU.</param>
/// <param name="Quantity">Units to allocate.</param>
public sealed record OrderCreatedLine(string ProductSku, int Quantity);

/// <summary>Published when an order moves to Paid. The worker fulfils it once stock is allocated.</summary>
/// <param name="OrderId">The paid order.</param>
/// <param name="PaidAt">UTC.</param>
public sealed record OrderPaid(Guid OrderId, DateTime PaidAt);
