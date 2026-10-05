using ErrorOr;
using OrderManagement.Domain.Common;

namespace OrderManagement.Domain.Customers;

public static class CustomerErrors
{
    public static readonly Error NameRequired = Error.Validation(
        code: "Customer.NameRequired",
        description: "Please enter the customer's name.",
        metadata: ErrorMetadata.ForField("name"));

    public static readonly Error EmailRequired = Error.Validation(
        code: "Customer.EmailRequired",
        description: "Please enter the customer's email address.",
        metadata: ErrorMetadata.ForField("email"));

    public static Error NotFound(Guid id) => Error.NotFound(
        code: "Customer.NotFound",
        description: $"We couldn't find a customer with ID {id}.");
}
