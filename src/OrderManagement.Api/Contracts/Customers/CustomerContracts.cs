using OrderManagement.Api.Common.Paging;

namespace OrderManagement.Api.Contracts.Customers;

/// <param name="Name">Full name or company name, up to 200 characters.</param>
/// <param name="Email">Unique (case-insensitive) email address, up to 320 characters.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2 code of a SADC member state, e.g. <c>ZA</c>.</param>
public sealed record CreateCustomerRequest(string? Name, string? Email, string? CountryCode);

/// <param name="Id">Customer ID.</param>
/// <param name="Name">Name as entered (trimmed).</param>
/// <param name="Email">Email, lower-cased.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2, upper case.</param>
/// <param name="CreatedAt">UTC.</param>
public sealed record CustomerResponse(Guid Id, string Name, string Email, string CountryCode, DateTime CreatedAt);

/// <summary><c>GET /api/v1/customers?search=&amp;page=&amp;pageSize=&amp;sort=</c></summary>
public sealed class CustomerListQuery : PageQuery
{
    public const string SortByName = "name";
    public const string SortByCreatedAt = "createdAt";

    /// <summary>Matches customers whose name or email <b>starts with</b> this text (case-insensitive).</summary>
    public string? Search { get; init; }

    /// <summary><c>name</c> (default) or <c>createdAt</c>; prefix with <c>-</c> for descending.</summary>
    public string? Sort { get; init; }
}
