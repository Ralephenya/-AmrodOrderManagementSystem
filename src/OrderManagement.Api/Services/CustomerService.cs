using ErrorOr;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Common.Paging;
using OrderManagement.Api.Contracts.Customers;
using OrderManagement.Api.Services.Interfaces;
using OrderManagement.Domain.Customers;
using OrderManagement.Infrastructure.Persistence;

namespace OrderManagement.Api.Services;

public sealed class CustomerService(AppDbContext db, TimeProvider clock) : ICustomerService
{
    private const string EmailIndex = "IX_Customers_Email";

    public async Task<ErrorOr<CustomerResponse>> CreateAsync(CreateCustomerRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = Customer.Create(request.Name, request.Email, request.CountryCode, clock);
        if (created.IsError)
        {
            return created.Errors;
        }

        var customer = created.Value;

        // Friendly fast path. The unique index below is what actually guarantees uniqueness under concurrency.
        if (await db.Customers.AsNoTracking().AnyAsync(c => c.Email == customer.Email, ct))
        {
            return CustomerErrors.EmailAlreadyExists(customer.Email);
        }

        db.Customers.Add(customer);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(EmailIndex))
        {
            return CustomerErrors.EmailAlreadyExists(customer.Email);
        }

        return ToResponse(customer);
    }

    public async Task<ErrorOr<CustomerResponse>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var customer = await db.Customers
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CustomerResponse(c.Id, c.Name, c.Email, c.CountryCode, c.CreatedAt))
            .SingleOrDefaultAsync(ct);

        return customer is null ? CustomerErrors.NotFound(id) : customer;
    }

    public Task<PagedResult<CustomerResponse>> ListAsync(CustomerListQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var customers = db.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Prefix match ("starts with") so SQL Server can seek IX_Customers_Name / IX_Customers_Email.
            // A contains-match ('%text%') would scan the whole table. Wildcards typed by the user are escaped.
            var pattern = EscapeLike(query.Search.Trim()) + "%";
            var emailPattern = EscapeLike(query.Search.Trim().ToLowerInvariant()) + "%";
            customers = customers.Where(c =>
                EF.Functions.Like(c.Name, pattern, LikeEscape) || EF.Functions.Like(c.Email, emailPattern, LikeEscape));
        }

        var sort = SortSpec.Parse(query.Sort, new SortSpec(CustomerListQuery.SortByName, Descending: false));
        var ordered = sort.Is(CustomerListQuery.SortByCreatedAt)
            ? (sort.Descending ? customers.OrderByDescending(c => c.CreatedAt) : customers.OrderBy(c => c.CreatedAt))
            : (sort.Descending ? customers.OrderByDescending(c => c.Name) : customers.OrderBy(c => c.Name));

        return ordered
            .ThenBy(c => c.Id) // unique tie-breaker so pages never overlap
            .Select(c => new CustomerResponse(c.Id, c.Name, c.Email, c.CountryCode, c.CreatedAt))
            .ToPagedResultAsync(query, ct);
    }

    private const string LikeEscape = "\\";

    private static string EscapeLike(string value) => value
        .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
        .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
        .Replace("_", LikeEscape + "_", StringComparison.Ordinal)
        .Replace("[", LikeEscape + "[", StringComparison.Ordinal);

    private static CustomerResponse ToResponse(Customer c) => new(c.Id, c.Name, c.Email, c.CountryCode, c.CreatedAt);
}
