namespace OrderManagement.Domain.Orders;

/// <summary>A requested line on a new order, before validation.</summary>
public sealed record NewOrderLine(string? ProductSku, int Quantity, decimal UnitPrice);
