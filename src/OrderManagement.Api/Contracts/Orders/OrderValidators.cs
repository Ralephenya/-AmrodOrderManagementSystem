using FluentValidation;
using OrderManagement.Api.Common.Paging;
using OrderManagement.Domain.Orders;

namespace OrderManagement.Api.Contracts.Orders;

/// <summary>
/// Shape checks with friendly messages. Business rules (currency allowed for the customer's country, price precision
/// for the currency, duplicate SKUs, totals) live in the <see cref="Order"/> aggregate and are enforced there.
/// </summary>
public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(r => r.CustomerId)
            .NotEmpty().WithMessage("Please choose the customer this order is for.");

        RuleFor(r => r.CurrencyCode)
            .NotEmpty().WithMessage("Please choose a currency, such as ZAR.")
            .Length(3).WithMessage("Currency must be a three-letter code, such as ZAR.");

        RuleFor(r => r.LineItems)
            .NotEmpty().WithMessage("An order needs at least one line item.")
            .Must(lines => lines is null || lines.Count <= Order.MaxLineItems)
            .WithMessage($"An order can have at most {Order.MaxLineItems} line items.");

        RuleForEach(r => r.LineItems).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductSku)
                .NotEmpty().WithMessage("Please enter a product SKU.")
                .MaximumLength(OrderLineItem.ProductSkuMaxLength)
                .WithMessage($"Product SKU can be at most {OrderLineItem.ProductSkuMaxLength} characters.");

            line.RuleFor(l => l.Quantity)
                .GreaterThan(0).WithMessage("Quantity must be at least 1.");

            line.RuleFor(l => l.UnitPrice)
                .GreaterThanOrEqualTo(0).WithMessage("Unit price can't be negative.");
        });
    }
}

public sealed class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
{
    public UpdateOrderStatusRequestValidator()
    {
        RuleFor(r => r.Status)
            .Must(OrderStatusParser.IsValid)
            .WithMessage($"Status must be one of {string.Join(", ", Enum.GetNames<OrderStatus>())}.");
    }
}

public sealed class OrderListQueryValidator : AbstractValidator<OrderListQuery>
{
    public OrderListQueryValidator()
    {
        Include(new PageQueryValidator());

        RuleFor(q => q.Status)
            .Must(status => string.IsNullOrWhiteSpace(status) || OrderStatusParser.IsValid(status))
            .WithMessage($"Status must be one of {string.Join(", ", Enum.GetNames<OrderStatus>())}.");

        RuleFor(q => q.Sort).MustBeSortableBy(OrderListQuery.SortByCreatedAt, OrderListQuery.SortByTotal);
    }
}

/// <summary>Case-insensitive status names only. Numeric strings ("2") are rejected so the contract stays readable.</summary>
public static class OrderStatusParser
{
    public static bool IsValid(string? value) => TryParse(value, out _);

    public static bool TryParse(string? value, out OrderStatus status)
    {
        status = default;
        return !string.IsNullOrWhiteSpace(value)
            && !value.Trim().All(char.IsAsciiDigit)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out status)
            && Enum.IsDefined(status);
    }
}
