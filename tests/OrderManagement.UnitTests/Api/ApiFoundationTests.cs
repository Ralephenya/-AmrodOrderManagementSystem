using ErrorOr;
using OrderManagement.Api.Common.Errors;
using OrderManagement.Api.Common.Http;
using OrderManagement.Api.Common.Paging;
using OrderManagement.Api.Common.Validation;
using OrderManagement.Domain.Common;

namespace OrderManagement.UnitTests.Api;

public class ApiFoundationTests
{
    [Theory]
    [InlineData("Order.InvalidStatusTransition", "invalid_status_transition")]
    [InlineData("Customer.NotFound", "not_found")]
    [InlineData("Order.Cancelled", "cancelled")]
    [InlineData("NoDot", "no_dot")]
    public void ToWireCode_TakesTheLastSegmentInSnakeCase(string domainCode, string expected)
    {
        ProblemCatalog.ToWireCode(domainCode).ShouldBe(expected);
    }

    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.Failure, 422)]
    [InlineData(ErrorType.Unexpected, 500)]
    public void StatusFor_MapsEveryErrorType(ErrorType type, int status)
    {
        ErrorOrProblems.StatusFor(type).ShouldBe(status);
    }

    [Fact]
    public void ToFieldErrors_GroupsByFieldMetadata_FallingBackToTheCode()
    {
        var errors = ErrorOrProblems.ToFieldErrors(
        [
            Error.Validation("Order.SkuRequired", "Line 1: SKU.", ErrorMetadata.ForField("lineItems[0].productSku")),
            Error.Validation("Order.SkuTooLong", "Line 1: too long.", ErrorMetadata.ForField("lineItems[0].productSku")),
            Error.Validation("Order.LineItemsRequired", "Need lines."),
        ]);

        errors["lineItems[0].productSku"].ShouldBe(["Line 1: SKU.", "Line 1: too long."]);
        errors["lineItemsRequired"].ShouldBe(["Need lines."]);
    }

    [Theory]
    [InlineData("LineItems[0].UnitPrice", "lineItems[0].unitPrice")]
    [InlineData("Name", "name")]
    [InlineData("PageSize", "pageSize")]
    public void JsonFieldPath_CamelCasesEverySegment(string property, string expected)
    {
        FluentValidationFilter.JsonFieldPath(property).ShouldBe(expected);
    }

    [Theory]
    [InlineData("web-1234_abc.5", true)]
    [InlineData("0af7651916cd43dd8448eb211c80319c", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("has space", false)]
    [InlineData("line\nbreak", false)]
    [InlineData("<script>", false)]
    public void CorrelationId_OnlyTrustsShortPlainValues(string? value, bool expected)
    {
        CorrelationIdMiddleware.IsSafe(value).ShouldBe(expected);
        CorrelationIdMiddleware.IsSafe(new string('a', 65)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, "createdAt", true)]
    [InlineData("  ", "createdAt", true)]
    [InlineData("total", "total", false)]
    [InlineData("-total", "total", true)]
    [InlineData(" -createdAt ", "createdAt", true)]
    public void SortSpec_Parse(string? value, string field, bool descending)
    {
        var spec = SortSpec.Parse(value, new SortSpec("createdAt", Descending: true));

        spec.Field.ShouldBe(field);
        spec.Descending.ShouldBe(descending);
    }

    [Theory]
    // page, pageSize, totalCount, totalPages, hasPrevious, hasNext
    [InlineData(1, 20, 0, 0, false, false)]
    [InlineData(1, 20, 1, 1, false, false)]
    [InlineData(1, 20, 21, 2, false, true)]
    [InlineData(2, 20, 40, 2, true, false)]
    [InlineData(2, 20, 41, 3, true, true)]
    public void PagedResult_ComputesNavigation(
        int page, int pageSize, int totalCount, int totalPages, bool hasPrevious, bool hasNext)
    {
        var result = new PagedResult<int>([], page, pageSize, totalCount);

        result.TotalPages.ShouldBe(totalPages);
        result.HasPrevious.ShouldBe(hasPrevious);
        result.HasNext.ShouldBe(hasNext);
    }
}
