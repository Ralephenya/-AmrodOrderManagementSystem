using HotChocolate;
using HotChocolate.Types;
using OrderManagement.Domain.Orders;

namespace OrderManagement.Api.GraphQL;

// The read model GraphQL exposes. These are plain classes with init-only properties (not the domain entities, not
// positional records) so that EF Core can translate the filtering, sorting and projection Hot Chocolate composes on
// top of them into a single SQL query.

/// <summary>An order with its line items.</summary>
[GraphQLName("Order")]
public sealed class OrderNode
{
    public Guid Id { get; init; }

    public Guid CustomerId { get; init; }

    public OrderStatus Status { get; init; }

    /// <summary>ISO 4217. Amounts on this order and its lines are all in this currency.</summary>
    public string CurrencyCode { get; init; } = string.Empty;

    /// <summary>Σ(quantity × unitPrice), computed by the server.</summary>
    public decimal TotalAmount { get; init; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>UTC time the worker allocated stock, or null.</summary>
    public DateTime? AllocatedAt { get; init; }

    public IReadOnlyCollection<OrderLineNode> LineItems { get; init; } = [];
}

/// <summary>One line of an order. Exposed as <c>OrderLineItem</c>, with <c>lineTotal</c> added by <see cref="OrderLineNodeType"/>.</summary>
public sealed class OrderLineNode
{
    public Guid Id { get; init; }

    public string ProductSku { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }
}

/// <summary>
/// Adds <c>lineTotal</c>, computed in C# from the line's own quantity and unit price. Computing it in SQL would return
/// four decimal places (SQL Server widens int × decimal(18,2)), while money everywhere else in the API has the
/// currency's own decimals. Quantity and unit price are always projected so the total can be computed even when the
/// query doesn't ask for them.
/// </summary>
internal sealed class OrderLineNodeType : ObjectType<OrderLineNode>
{
    protected override void Configure(IObjectTypeDescriptor<OrderLineNode> descriptor)
    {
        descriptor.Name("OrderLineItem");
        descriptor.Field(l => l.Quantity).IsProjected(true);
        descriptor.Field(l => l.UnitPrice).IsProjected(true);
        descriptor.Field("lineTotal")
            .Type<NonNullType<DecimalType>>()
            .Description("quantity × unitPrice, in the order's currency.")
            .Resolve(context =>
            {
                var line = context.Parent<OrderLineNode>();
                return line.Quantity * line.UnitPrice;
            });
    }
}
