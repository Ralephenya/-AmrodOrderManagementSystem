using Dapper;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Domain.Orders;
using OrderManagement.Infrastructure.Persistence;

namespace OrderManagement.Infrastructure.Reports;

/// <summary>
/// Read-only reporting queries, hand-written in SQL and run with Dapper. EF Core owns the schema and every write. Reports
/// are where explicit, reviewable SQL (window functions, carefully placed join filters) beats LINQ translation. These are
/// the same queries as the written answers to SQL questions 13 and 18 in ANSWERS.md.
/// </summary>
public interface IOrderReportQueries
{
    Task<IReadOnlyList<TopSpenderRow>> TopSpendersAsync(
        string currencyCode, IReadOnlyCollection<string> countryCodes, DateTime sinceUtc, int top, CancellationToken ct);

    Task<IReadOnlyList<RunningTotalRow>> RunningTotalsAsync(Guid customerId, string currencyCode, CancellationToken ct);
}

public sealed record TopSpenderRow(Guid CustomerId, string Name, string CountryCode, decimal TotalSpend, int OrderCount);

public sealed record RunningTotalRow(Guid OrderId, DateTime CreatedAt, string Status, decimal TotalAmount, decimal RunningTotal);

/// <remarks>
/// "Spend" means money actually committed: orders that are <b>Paid</b> or <b>Fulfilled</b>. Pending orders haven't been paid,
/// and cancelled orders never will be. Amounts are only ever summed within a single currency.
/// </remarks>
internal sealed class OrderReportQueries(AppDbContext db) : IOrderReportQueries
{
    // Inlined as literals (they're constants, not user input): string parameters would be sent as nvarchar and force an
    // implicit conversion on the varchar Status column, which can stop the optimizer seeking on the index.
    private const string SpendStatuses = $"'{nameof(OrderStatus.Paid)}', '{nameof(OrderStatus.Fulfilled)}'";

    // Q13. The order filters sit in the LEFT JOIN's ON clause, not in WHERE: a WHERE on o.* would discard the
    // NULL-extended rows and silently drop every customer with no qualifying orders. COALESCE turns their NULL sum into 0.
    // Customers are limited to countries that can order in the currency, so a ZAR report doesn't list Botswana at 0.
    internal const string TopSpendersSql = $"""
        SELECT TOP (@Top)
               c.Id                             AS CustomerId,
               c.Name                           AS Name,
               c.CountryCode                    AS CountryCode,
               COALESCE(SUM(o.TotalAmount), 0)  AS TotalSpend,
               COUNT(o.Id)                      AS OrderCount
        FROM Customers AS c
        LEFT JOIN Orders AS o
               ON  o.CustomerId   = c.Id
               AND o.CurrencyCode = @Currency
               AND o.Status IN ({SpendStatuses})
               AND o.CreatedAt   >= @Since
        WHERE c.CountryCode IN @Countries
        GROUP BY c.Id, c.Name, c.CountryCode
        ORDER BY TotalSpend DESC, c.Name, c.Id;
        """;

    // Q18. ROWS (not the default RANGE) so the frame is exactly "every earlier row plus this one": orders with
    // identical timestamps are not lumped together, and SQL Server can use the cheaper in-memory window spool.
    // Ordering by (CreatedAt, Id) makes ties deterministic.
    internal const string RunningTotalsSql = $"""
        SELECT o.Id           AS OrderId,
               o.CreatedAt    AS CreatedAt,
               o.Status       AS Status,
               o.TotalAmount  AS TotalAmount,
               SUM(o.TotalAmount) OVER (
                   PARTITION BY o.CustomerId
                   ORDER BY o.CreatedAt, o.Id
                   ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS RunningTotal
        FROM Orders AS o
        WHERE o.CustomerId   = @CustomerId
          AND o.CurrencyCode = @Currency
          AND o.Status IN ({SpendStatuses})
        ORDER BY o.CreatedAt, o.Id;
        """;

    public async Task<IReadOnlyList<TopSpenderRow>> TopSpendersAsync(
        string currencyCode, IReadOnlyCollection<string> countryCodes, DateTime sinceUtc, int top, CancellationToken ct)
    {
        var rows = await WithRetriesAsync(connection => connection.QueryAsync<TopSpenderRow>(new CommandDefinition(
            TopSpendersSql,
            new
            {
                Top = top,
                Currency = new DbString { Value = currencyCode, IsAnsi = true, IsFixedLength = true, Length = 3 },
                Since = sinceUtc,
                Countries = countryCodes.Select(Ansi).ToList(),
            },
            cancellationToken: ct)), ct);

        return [.. rows];
    }

    public async Task<IReadOnlyList<RunningTotalRow>> RunningTotalsAsync(Guid customerId, string currencyCode, CancellationToken ct)
    {
        var rows = await WithRetriesAsync(connection => connection.QueryAsync<RunningTotalRow>(new CommandDefinition(
            RunningTotalsSql,
            new
            {
                CustomerId = customerId,
                Currency = new DbString { Value = currencyCode, IsAnsi = true, IsFixedLength = true, Length = 3 },
            },
            cancellationToken: ct)), ct);

        // datetime2 has no time zone; the schema stores UTC (see UtcDateTimeConverter), so stamp the Kind like EF does.
        return [.. rows.Select(r => r with { CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc) })];
    }

    /// <summary>
    /// Runs a Dapper query on the DbContext's connection (one connection string, one pool, opened and closed as needed)
    /// inside EF's execution strategy, so reports get the same transient-fault retries (failovers, throttling) as every
    /// EF query. Safe because the queries are read-only.
    /// </summary>
    private Task<T> WithRetriesAsync<T>(Func<System.Data.Common.DbConnection, Task<T>> query, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(
            query,
            (_, q, _) => q(db.Database.GetDbConnection()),
            verifySucceeded: null,
            ct);

    /// <summary>char/varchar columns get char/varchar parameters, so comparisons need no conversion.</summary>
    private static DbString Ansi(string value) => new() { Value = value, IsAnsi = true, IsFixedLength = true, Length = value.Length };
}
