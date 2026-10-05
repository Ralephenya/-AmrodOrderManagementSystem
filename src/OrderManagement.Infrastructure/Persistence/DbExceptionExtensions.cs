using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace OrderManagement.Infrastructure.Persistence;

public static class DbExceptionExtensions
{
    // 2601: duplicate key in a unique index. 2627: violation of a unique/primary key constraint.
    private static readonly int[] UniqueViolationNumbers = [2601, 2627];

    /// <summary>
    /// True when the save failed because of a unique index, optionally a specific one. This is how a
    /// check-then-insert race (two requests creating the same email at once) is turned back into a clean 409.
    /// </summary>
    public static bool IsUniqueViolation(this DbUpdateException exception, string? indexName = null) =>
        exception.InnerException is SqlException sql
        && UniqueViolationNumbers.Contains(sql.Number)
        && (indexName is null || sql.Message.Contains(indexName, StringComparison.Ordinal));
}
