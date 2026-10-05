using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;

namespace OrderManagement.Api.Auth;

internal static class AuthenticationSetup
{
    public static IServiceCollection AddApiAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection(AuthOptions.SectionName);
        services.Configure<AuthOptions>(section);
        var options = section.Get<AuthOptions>() ?? new AuthOptions();

        if (options.Mode == AuthMode.Mock)
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException("Auth:Mode=Mock is not allowed in Production. Configure AzureAd.");
            }

            services.AddSingleton<MockTokenIssuer>();
            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(jwt =>
                {
                    jwt.MapInboundClaims = false; // keep Entra claim names ("roles", "oid") as-is
                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidIssuer = options.Mock.Issuer,
                        ValidAudience = options.Mock.Audience,
                        IssuerSigningKey = MockTokenIssuer.SigningKey(options.Mock),
                        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                        RoleClaimType = "roles",
                        NameClaimType = "name",
                        ClockSkew = TimeSpan.FromSeconds(30),
                    };
                });
        }
        else
        {
            // Validates issuer, audience, signature (tenant signing keys) and lifetime against the AzureAd section.
            // App roles arrive in the "roles" claim, which Identity.Web uses as the role claim type.
            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.OrdersRead, p => p.RequireAuthenticatedUser().RequireRole(AuthPolicies.ReadRoles))
            .AddPolicy(AuthPolicies.OrdersWrite, p => p.RequireAuthenticatedUser().RequireRole(AuthPolicies.WriteRoles))
            .AddPolicy(AuthPolicies.OrdersAdmin, p => p.RequireAuthenticatedUser().RequireRole(AuthPolicies.AdminRoles))
            // Secure by default: an endpoint without [Authorize]/[AllowAnonymous] still requires a signed-in user.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
