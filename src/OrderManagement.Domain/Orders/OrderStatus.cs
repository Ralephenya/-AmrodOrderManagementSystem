namespace OrderManagement.Domain.Orders;

/// <summary>
/// Order lifecycle: <c>Pending → Paid → Fulfilled</c>, with <c>Pending | Paid → Cancelled</c>.
/// <see cref="Fulfilled"/> and <see cref="Cancelled"/> are terminal.
/// </summary>
public enum OrderStatus
{
    Pending = 0,
    Paid = 1,
    Fulfilled = 2,
    Cancelled = 3,
}
