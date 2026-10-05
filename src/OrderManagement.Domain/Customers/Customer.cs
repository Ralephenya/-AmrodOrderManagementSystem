using ErrorOr;
using OrderManagement.Domain.Sadc;

namespace OrderManagement.Domain.Customers;

public sealed class Customer
{
    public const int NameMaxLength = 200;
    public const int EmailMaxLength = 320;

    // For EF Core materialisation.
    private Customer()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Stored trimmed and lower-cased so uniqueness checks are case-insensitive.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 code of a SADC member state, upper case.</summary>
    public string CountryCode { get; private set; } = string.Empty;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Creates a customer. Shape rules (lengths, email format) are checked by the API validators;
    /// this enforces the invariants that must hold no matter who calls it.
    /// </summary>
    public static ErrorOr<Customer> Create(string? name, string? email, string? countryCode, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        List<Error> errors = [];

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(CustomerErrors.NameRequired);
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            errors.Add(CustomerErrors.EmailRequired);
        }

        var country = SadcCatalogue.FindCountry(countryCode);
        if (country.IsError)
        {
            errors.AddRange(country.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        return new Customer
        {
            Name = name!.Trim(),
            Email = email!.Trim().ToLowerInvariant(),
            CountryCode = country.Value.Code,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
    }
}
