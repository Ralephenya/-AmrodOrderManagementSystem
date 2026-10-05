namespace OrderManagement.Domain.Orders;

public sealed class OrderLineItem
{
    public const int ProductSkuMaxLength = 64;

    // For EF Core materialisation.
    private OrderLineItem()
    {
    }

    internal OrderLineItem(string productSku, int quantity, decimal unitPrice)
    {
        ProductSku = productSku;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    /// <summary>Trimmed and upper case.</summary>
    public string ProductSku { get; private set; } = string.Empty;

    /// <summary>Always greater than zero.</summary>
    public int Quantity { get; private set; }

    /// <summary>In the order's currency, never negative, never more decimals than the currency allows.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>Exact, because <see cref="UnitPrice"/> is validated to the currency's precision.</summary>
    public decimal LineTotal => Quantity * UnitPrice;
}
