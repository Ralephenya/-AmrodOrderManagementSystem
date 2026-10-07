using HotChocolate.Authorization;
using HotChocolate.Data;
using HotChocolate.Data.Sorting;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Auth;
using OrderManagement.Infrastructure.Persistence;

namespace OrderManagement.Api.GraphQL;

/// <summary>
/// The read-only GraphQL query root: a customer's orders with nested line items, filtered, sorted and paged.
/// </summary>
/// <remarks>
/// The resolver returns an <see cref="IQueryable{T}"/>. Hot Chocolate applies the client's filter, sort, page and field
/// selection to it, and EF Core turns the whole thing into one SQL query that reads only the requested columns, line
/// items included, so there is no N+1. Each resolver gets its own <see cref="AppDbContext"/> from the factory, because
/// GraphQL executes resolvers in parallel and a context isn't thread-safe.
/// </remarks>
public sealed class OrderQueries
{
    /// <summary>A customer's orders, newest first unless <c>order</c> says otherwise.</summary>
    /// <param name="customerId">The customer whose orders to return. An unknown customer has no orders.</param>
    /// <param name="db">Injected per resolver by Hot Chocolate.</param>
    /// <param name="sorting">The client's <c>order</c> argument. <c>[UseSorting]</c> still applies it (see below).</param>
    [Authorize(Policy = AuthPolicies.OrdersRead)]
    [UsePaging(MaxPageSize = 100, DefaultPageSize = 20, IncludeTotalCount = true)]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<OrderNode> GetOrdersByCustomer(Guid customerId, AppDbContext db, ISortingContext sorting)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(sorting);

        // Paging is offset-based under the cursor, so the order must be total. A client sort on a non-unique field
        // (equal totals, the same status) gets Id as its last key, or tied orders could swap places between page
        // queries: one appearing twice, another never. Accessing the sorting context marks sorting as handled by the
        // resolver, so Handled(false) first hands it back to [UseSorting], or the client's order would be ignored.
        sorting.Handled(false);
        sorting.OnAfterSortingApplied<IQueryable<OrderNode>>(static (userDefinedSorting, query) =>
            userDefinedSorting ? ((IOrderedQueryable<OrderNode>)query).ThenByDescending(o => o.Id) : query);

        return db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            // The default order, total thanks to Id. A client "order" argument replaces it (see above).
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Select(o => new OrderNode
            {
                Id = o.Id,
                CustomerId = o.CustomerId,
                Status = o.Status,
                CurrencyCode = o.CurrencyCode,
                TotalAmount = o.TotalAmount,
                CreatedAt = o.CreatedAt,
                AllocatedAt = o.AllocatedAt,
                LineItems = o.LineItems
                    .OrderBy(li => li.ProductSku)
                    .Select(li => new OrderLineNode
                    {
                        Id = li.Id,
                        ProductSku = li.ProductSku,
                        Quantity = li.Quantity,
                        UnitPrice = li.UnitPrice,
                    })
                    .ToList(),
            });
    }
}
