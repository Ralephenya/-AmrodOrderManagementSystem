using ErrorOr;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Sadc;

namespace OrderManagement.Domain.Orders;

/// <summary>Order errors, worded for end users. Line numbers in messages are 1-based.</summary>
public static class OrderErrors
{
    private const string LineItemsField = "lineItems";

    public static Error NotFound(Guid id) => Error.NotFound(
        code: "Order.NotFound",
        description: $"We couldn't find an order with ID {id}.");

    public static readonly Error LineItemsRequired = Error.Validation(
        code: "Order.LineItemsRequired",
        description: "An order needs at least one line item.",
        metadata: ErrorMetadata.ForField(LineItemsField));

    public static Error TooManyLineItems(int max) => Error.Validation(
        code: "Order.TooManyLineItems",
        description: $"An order can have at most {max} line items.",
        metadata: ErrorMetadata.ForField(LineItemsField));

    public static Error SkuRequired(int index) => Error.Validation(
        code: "Order.SkuRequired",
        description: $"Line {index + 1}: please enter a product SKU.",
        metadata: ErrorMetadata.ForField(LineField(index, "productSku")));

    public static Error SkuTooLong(int index) => Error.Validation(
        code: "Order.SkuTooLong",
        description: $"Line {index + 1}: the product SKU can be at most {OrderLineItem.ProductSkuMaxLength} characters.",
        metadata: ErrorMetadata.ForField(LineField(index, "productSku")));

    public static Error DuplicateSku(string sku) => Error.Validation(
        code: "Order.DuplicateSku",
        description: $"SKU {sku} appears on more than one line. Combine them into a single line with the total quantity.",
        metadata: ErrorMetadata.ForField(LineItemsField));

    public static Error QuantityMustBePositive(int index) => Error.Validation(
        code: "Order.QuantityMustBePositive",
        description: $"Line {index + 1}: quantity must be at least 1.",
        metadata: ErrorMetadata.ForField(LineField(index, "quantity")));

    public static Error UnitPriceNegative(int index) => Error.Validation(
        code: "Order.UnitPriceNegative",
        description: $"Line {index + 1}: unit price can't be negative.",
        metadata: ErrorMetadata.ForField(LineField(index, "unitPrice")));

    public static Error UnitPriceTooPrecise(int index, Currency currency) => Error.Validation(
        code: "Order.UnitPriceTooPrecise",
        description: currency.MinorUnits == 0
            ? $"Line {index + 1}: {currency.Code} prices can't have decimals."
            : $"Line {index + 1}: {currency.Code} prices can have at most {currency.MinorUnits} decimal places.",
        metadata: ErrorMetadata.ForField(LineField(index, "unitPrice")));

    public static readonly Error TotalTooLarge = Error.Validation(
        code: "Order.TotalTooLarge",
        description: "The order total is too large to process. Split it into smaller orders.",
        metadata: ErrorMetadata.ForField(LineItemsField));

    public static Error InvalidStatus(OrderStatus status) => Error.Validation(
        code: "Order.InvalidStatus",
        description: $"'{status}' isn't a valid order status. Use Pending, Paid, Fulfilled or Cancelled.",
        metadata: ErrorMetadata.ForField("status"));

    public static Error AlreadyInStatus(OrderStatus status) => Error.Conflict(
        code: "Order.AlreadyInStatus",
        description: $"This order is already {status}.");

    public static Error InvalidStatusTransition(OrderStatus from, OrderStatus to, IReadOnlyList<OrderStatus> allowed) =>
        Error.Conflict(
            code: "Order.InvalidStatusTransition",
            description: allowed.Count == 0
                ? $"A {from} order can't be changed any more."
                : $"A {from} order can't be moved to {to}. It can only be moved to {string.Join(" or ", allowed)}.");

    public static readonly Error NotAllocated = Error.Conflict(
        code: "Order.NotAllocated",
        description: "This order can't be fulfilled until its stock has been allocated. Try again shortly.");

    public static readonly Error CancelledCannotAllocate = Error.Conflict(
        code: "Order.Cancelled",
        description: "This order was cancelled, so stock won't be allocated.");

    private static string LineField(int index, string property) => $"{LineItemsField}[{index}].{property}";
}
