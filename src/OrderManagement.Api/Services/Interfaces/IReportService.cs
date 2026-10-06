using ErrorOr;
using OrderManagement.Api.Contracts.Reports;

namespace OrderManagement.Api.Services.Interfaces;

public interface IReportService
{
    Task<ErrorOr<TopSpendersResponse>> TopSpendersAsync(TopSpendersQuery query, CancellationToken ct);

    Task<ErrorOr<RunningTotalsResponse>> RunningTotalsAsync(Guid customerId, RunningTotalsQuery query, CancellationToken ct);
}
