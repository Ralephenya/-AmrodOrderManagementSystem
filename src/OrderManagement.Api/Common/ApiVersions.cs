using Asp.Versioning;

namespace OrderManagement.Api.Common;

/// <summary>
/// Every API version the service exposes, in one place. Controllers use the constants in attributes
/// (<c>[ApiVersion(ApiVersions.V1)]</c>), so adding or retiring a version is a single, searchable change.
/// </summary>
public static class ApiVersions
{
    public const string V1 = "1.0";

    /// <summary>Version used for unversioned routes (the brief's <c>/api/orders</c>) and when none is specified.</summary>
    public const string Current = V1;

    /// <summary>URL segment the unversioned routes are rewritten to, e.g. <c>/api/orders</c> → <c>/api/v1/orders</c>.</summary>
    public const string CurrentUrlSegment = "v1";

    public static ApiVersion Default { get; } = ApiVersionParser.Default.Parse(Current);
}
