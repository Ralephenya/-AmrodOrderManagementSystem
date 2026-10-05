using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace OrderManagement.Api.Auth;

/// <summary>
/// Issues tokens shaped like Entra access tokens (<c>roles</c>, <c>oid</c>, <c>name</c>, <c>aud</c>, <c>iss</c>),
/// so the API's authorization code runs exactly as it would against a real tenant.
/// </summary>
public sealed class MockTokenIssuer(IOptions<AuthOptions> options, TimeProvider clock)
{
    private readonly MockAuthOptions _mock = options.Value.Mock;

    public TimeSpan Lifetime => TimeSpan.FromMinutes(_mock.TokenLifetimeMinutes);

    public string Issue(string name, IEnumerable<string> roles)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _mock.Issuer,
            Audience = _mock.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(Lifetime),
            Claims = new Dictionary<string, object>
            {
                ["oid"] = DeterministicObjectId(name),
                ["sub"] = DeterministicObjectId(name),
                ["name"] = name,
                ["roles"] = roles.Distinct(StringComparer.Ordinal).ToArray(),
            },
            SigningCredentials = new SigningCredentials(SigningKey(_mock), SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    internal static SymmetricSecurityKey SigningKey(MockAuthOptions mock)
    {
        var bytes = Encoding.UTF8.GetBytes(mock.SigningKey);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Auth:Mock:SigningKey must be at least 32 bytes.");
        }

        return new SymmetricSecurityKey(bytes);
    }

    // Same name, same user ID, so data created in one session can be attributed in the next.
    private static string DeterministicObjectId(string name) =>
        new Guid(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name)).AsSpan(0, 16)).ToString();
}
