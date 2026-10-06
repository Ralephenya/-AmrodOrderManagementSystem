namespace OrderManagement.Worker.Consumers;

/// <summary>
/// The event names an order the database doesn't have. With the outbox this shouldn't happen (the event commits
/// with the order), so it is retried, then dead-lettered for a person to investigate.
/// </summary>
public sealed class OrderNotFoundException : Exception
{
    public OrderNotFoundException(Guid orderId)
        : base($"Order {orderId} was not found.")
    {
        OrderId = orderId;
    }

    public OrderNotFoundException()
    {
    }

    public OrderNotFoundException(string message)
        : base(message)
    {
    }

    public OrderNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Guid OrderId { get; }
}
