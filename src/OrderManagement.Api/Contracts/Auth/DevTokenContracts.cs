using FluentValidation;
using OrderManagement.Api.Auth;

namespace OrderManagement.Api.Contracts.Auth;

/// <param name="Name">Display name put in the token's <c>name</c> claim.</param>
/// <param name="Roles">App roles to grant: any of Orders.Read, Orders.Write, Orders.Admin.</param>
public sealed record DevTokenRequest(string? Name, IReadOnlyList<string>? Roles);

/// <param name="AccessToken">JWT to send as <c>Authorization: Bearer &lt;token&gt;</c>.</param>
/// <param name="TokenType">Always <c>Bearer</c>.</param>
/// <param name="ExpiresIn">Lifetime in seconds.</param>
/// <param name="Roles">Roles granted.</param>
public sealed record DevTokenResponse(string AccessToken, string TokenType, int ExpiresIn, IReadOnlyList<string> Roles);

public sealed class DevTokenRequestValidator : AbstractValidator<DevTokenRequest>
{
    public DevTokenRequestValidator()
    {
        RuleFor(r => r.Name)
            .MaximumLength(100).WithMessage("Name can be at most 100 characters.");

        RuleFor(r => r.Roles)
            .NotEmpty().WithMessage($"Choose at least one role: {string.Join(", ", AuthPolicies.AllRoles)}.");

        RuleForEach(r => r.Roles)
            .Must(role => AuthPolicies.AllRoles.Contains(role, StringComparer.Ordinal))
            .WithMessage((_, role) => $"'{role}' isn't a role. Use {string.Join(", ", AuthPolicies.AllRoles)}.");
    }
}
