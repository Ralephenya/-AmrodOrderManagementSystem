using FluentValidation;

namespace OrderManagement.Api.Common.Paging;

/// <summary>Query-string paging shared by every list endpoint: <c>?page=1&amp;pageSize=20</c>.</summary>
public class PageQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxPage = 100_000;

    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page, 1 to 100.</summary>
    public int PageSize { get; init; } = DefaultPageSize;

    internal int Skip => (Page - 1) * PageSize;
}

/// <summary>Rules every paged query inherits via <c>Include(new PageQueryValidator())</c>.</summary>
public sealed class PageQueryValidator : AbstractValidator<PageQuery>
{
    public PageQueryValidator()
    {
        RuleFor(q => q.Page)
            .InclusiveBetween(1, PageQuery.MaxPage)
            .WithMessage($"Page must be between 1 and {PageQuery.MaxPage:N0}.");

        RuleFor(q => q.PageSize)
            .InclusiveBetween(1, PageQuery.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {PageQuery.MaxPageSize}.");
    }
}
