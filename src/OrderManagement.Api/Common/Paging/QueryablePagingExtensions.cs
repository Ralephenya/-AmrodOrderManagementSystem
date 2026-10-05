using Microsoft.EntityFrameworkCore;

namespace OrderManagement.Api.Common.Paging;

public static class QueryablePagingExtensions
{
    /// <summary>
    /// Runs a COUNT and then one page of the (already filtered and sorted) query. The source must be ordered,
    /// with a unique tie-breaker such as <c>Id</c>, or rows can repeat or vanish between pages.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> source,
        PageQuery page,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);

        var total = await source.CountAsync(ct);
        var items = total == 0
            ? []
            : await source.Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);

        return new PagedResult<T>(items, page.Page, page.PageSize, total);
    }
}
