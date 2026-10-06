using FluentValidation;
using OrderManagement.Domain.Orders;
using OrderManagement.Domain.Sadc;

namespace OrderManagement.Api.Contracts.Reports;

/// <summary><c>GET /api/v1/reports/top-spenders?currency=ZAR&amp;days=90&amp;top=10</c></summary>
public sealed class TopSpendersQuery
{
    /// <summary>ISO 4217. Spend is never summed across currencies.</summary>
    public string? Currency { get; init; }

    /// <summary>Look-back window in days (1–366). Default 90.</summary>
    public int Days { get; init; } = 90;

    /// <summary>How many customers to return (1–100). Default 10.</summary>
    public int Top { get; init; } = 10;
}

/// <param name="CurrencyCode">The single currency all amounts are in.</param>
/// <param name="SinceUtc">Start of the window (inclusive).</param>
/// <param name="UntilUtc">When the report was produced.</param>
/// <param name="Customers">Ranked by spend; customers without qualifying orders appear with 0.</param>
public sealed record TopSpendersResponse(
    string CurrencyCode, DateTime SinceUtc, DateTime UntilUtc, IReadOnlyList<TopSpenderResponse> Customers);

/// <param name="Rank">1-based position.</param>
/// <param name="CustomerId">Customer ID.</param>
/// <param name="Name">Customer name.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2.</param>
/// <param name="TotalSpend">Sum of Paid and Fulfilled orders in the window.</param>
/// <param name="OrderCount">Number of those orders.</param>
public sealed record TopSpenderResponse(
    int Rank, Guid CustomerId, string Name, string CountryCode, decimal TotalSpend, int OrderCount);

/// <summary><c>GET /api/v1/reports/customers/{id}/running-totals?currency=ZAR</c></summary>
public sealed class RunningTotalsQuery
{
    /// <summary>ISO 4217. Must be a currency the customer's country may order in.</summary>
    public string? Currency { get; init; }
}

public sealed record RunningTotalsResponse(Guid CustomerId, string CurrencyCode, IReadOnlyList<RunningTotalPoint> Orders);

/// <param name="OrderId">Order ID.</param>
/// <param name="CreatedAt">UTC.</param>
/// <param name="Status">Paid or Fulfilled.</param>
/// <param name="TotalAmount">This order's total.</param>
/// <param name="RunningTotal">Cumulative spend up to and including this order.</param>
public sealed record RunningTotalPoint(Guid OrderId, DateTime CreatedAt, OrderStatus Status, decimal TotalAmount, decimal RunningTotal);

public sealed class TopSpendersQueryValidator : AbstractValidator<TopSpendersQuery>
{
    public TopSpendersQueryValidator()
    {
        RuleFor(q => q.Currency).MustBeASadcCurrency();

        RuleFor(q => q.Days)
            .InclusiveBetween(1, 366).WithMessage("Days must be between 1 and 366.");

        RuleFor(q => q.Top)
            .InclusiveBetween(1, 100).WithMessage("Top must be between 1 and 100.");
    }
}

public sealed class RunningTotalsQueryValidator : AbstractValidator<RunningTotalsQuery>
{
    public RunningTotalsQueryValidator()
    {
        RuleFor(q => q.Currency).MustBeASadcCurrency();
    }
}

internal static class ReportValidationExtensions
{
    public static IRuleBuilderOptions<T, string?> MustBeASadcCurrency<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .Must(code => SadcCatalogue.TryGetCurrency(code, out _))
            .WithMessage((_, code) => SadcErrors.CurrencyNotSupported(code).Description);
}
