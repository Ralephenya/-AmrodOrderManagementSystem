using FluentValidation;
using OrderManagement.Api.Common.Paging;
using OrderManagement.Domain.Customers;
using OrderManagement.Domain.Sadc;

namespace OrderManagement.Api.Contracts.Customers;

public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("Please enter the customer's name.")
            .MaximumLength(Customer.NameMaxLength)
            .WithMessage($"Name can be at most {Customer.NameMaxLength} characters.");

        RuleFor(r => r.Email)
            .NotEmpty().WithMessage("Please enter the customer's email address.")
            .MaximumLength(Customer.EmailMaxLength)
            .WithMessage($"Email can be at most {Customer.EmailMaxLength} characters.")
            .EmailAddress().WithMessage("Please enter a valid email address, like name@example.co.za.");

        RuleFor(r => r.CountryCode)
            .Must(code => SadcCatalogue.TryGetCountry(code, out _))
            .WithMessage(r => SadcErrors.CountryNotSupported(r.CountryCode).Description);
    }
}

public sealed class CustomerListQueryValidator : AbstractValidator<CustomerListQuery>
{
    public const int SearchMaxLength = 100;

    public CustomerListQueryValidator()
    {
        Include(new PageQueryValidator());

        RuleFor(q => q.Search)
            .MaximumLength(SearchMaxLength)
            .WithMessage($"Search text can be at most {SearchMaxLength} characters.");

        RuleFor(q => q.Sort).MustBeSortableBy(CustomerListQuery.SortByName, CustomerListQuery.SortByCreatedAt);
    }
}
