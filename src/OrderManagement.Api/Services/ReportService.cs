using ErrorOr;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Contracts.Reports;
using OrderManagement.Api.Services.Interfaces;
using OrderManagement.Domain.Customers;
using OrderManagement.Domain.Orders;
using OrderManagement.Domain.Sadc;
using OrderManagement.Infrastructure.Persistence;
using OrderManagement.Infrastructure.Reports;

namespace OrderManagement.Api.Services;

public sealed class ReportService(IOrderReportQueries reports, AppDbContext db, TimeProvider clock) : IReportService
{
    public async Task<ErrorOr<TopSpendersResponse>> TopSpendersAsync(TopSpendersQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The HTTP validator checks this too; the service must not depend on having been called through it.
        if (!SadcCatalogue.TryGetCurrency(query.Currency, out var currency))
        {
            return SadcErrors.CurrencyNotSupported(query.Currency);
        }

        var until = clock.GetUtcNow().UtcDateTime;
        var since = until.AddDays(-query.Days);

        // Only customers who can order in this currency (e.g. ZAR: ZA, NA, LS, SZ) are ranked.
        var countries = SadcCatalogue.Countries.Where(c => c.Accepts(currency.Code)).Select(c => c.Code).ToList();

        var rows = await reports.TopSpendersAsync(currency.Code, countries, since, query.Top, ct);

        return new TopSpendersResponse(
            currency.Code,
            since,
            until,
            [.. rows.Select((r, i) => new TopSpenderResponse(i + 1, r.CustomerId, r.Name, r.CountryCode, r.TotalSpend, r.OrderCount))]);
    }

    public async Task<ErrorOr<RunningTotalsResponse>> RunningTotalsAsync(
        Guid customerId, RunningTotalsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var countryCode = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => c.CountryCode)
            .SingleOrDefaultAsync(ct);
        if (countryCode is null)
        {
            return CustomerErrors.NotFound(customerId);
        }

        // Same rule as placing an order: a Botswana customer has no ZAR history to report.
        var currency = SadcCatalogue.ValidateCurrencyForCountry(countryCode, query.Currency);
        if (currency.IsError)
        {
            return currency.Errors;
        }

        var rows = await reports.RunningTotalsAsync(customerId, currency.Value.Code, ct);

        return new RunningTotalsResponse(
            customerId,
            currency.Value.Code,
            [.. rows.Select(r => new RunningTotalPoint(
                r.OrderId, r.CreatedAt, Enum.Parse<OrderStatus>(r.Status), r.TotalAmount, r.RunningTotal))]);
    }
}
