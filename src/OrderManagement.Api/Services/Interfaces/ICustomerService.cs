using ErrorOr;
using OrderManagement.Api.Common.Paging;
using OrderManagement.Api.Contracts.Customers;

namespace OrderManagement.Api.Services.Interfaces;

public interface ICustomerService
{
    Task<ErrorOr<CustomerResponse>> CreateAsync(CreateCustomerRequest request, CancellationToken ct);

    Task<ErrorOr<CustomerResponse>> GetByIdAsync(Guid id, CancellationToken ct);

    Task<PagedResult<CustomerResponse>> ListAsync(CustomerListQuery query, CancellationToken ct);
}
