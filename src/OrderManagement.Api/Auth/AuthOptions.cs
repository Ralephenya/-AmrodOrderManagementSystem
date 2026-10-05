namespace OrderManagement.Api.Auth;

/// <summary>Bound from the <c>Auth</c> configuration section.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// <see cref="AuthMode.Entra"/> validates real Microsoft Entra ID tokens via Microsoft.Identity.Web
    /// (configured under <c>AzureAd</c>). <see cref="AuthMode.Mock"/> validates tokens signed with a local key,
    /// for development and tests when no tenant is available. Mock mode refuses to start in Production.
    /// </summary>
    public AuthMode Mode { get; init; } = AuthMode.Entra;

    public MockAuthOptions Mock { get; init; } = new();
}

public enum AuthMode
{
    Entra,
    Mock,
}

public sealed class MockAuthOptions
{
    public string Issuer { get; init; } = "https://login.mock-entra.local/order-management/v2.0";

    public string Audience { get; init; } = "api://amrod-order-management";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Development and test values only; never a real secret.</summary>
    public string SigningKey { get; init; } = string.Empty;

    public int TokenLifetimeMinutes { get; init; } = 60;
}
