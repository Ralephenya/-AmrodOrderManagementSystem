namespace OrderManagement.Api.Auth;

/// <summary>
/// App roles (as defined on the Entra app registration) and the policies built from them.
/// Roles are hierarchical: Admin can do everything Write can, and Write can do everything Read can.
/// </summary>
public static class AuthPolicies
{
    public const string OrdersRead = "Orders.Read";
    public const string OrdersWrite = "Orders.Write";
    public const string OrdersAdmin = "Orders.Admin";

    public static readonly IReadOnlyList<string> AllRoles = [OrdersRead, OrdersWrite, OrdersAdmin];

    internal static readonly string[] ReadRoles = [OrdersRead, OrdersWrite, OrdersAdmin];
    internal static readonly string[] WriteRoles = [OrdersWrite, OrdersAdmin];
    internal static readonly string[] AdminRoles = [OrdersAdmin];
}
