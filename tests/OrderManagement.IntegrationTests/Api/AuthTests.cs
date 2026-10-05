using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.IntegrationTests.Fixtures;

namespace OrderManagement.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class AuthTests(IntegrationTestFixture fixture)
{
    private const string Countries = "/api/v1/reference/countries";

    [Fact]
    public async Task NoToken_Returns401_FriendlyProblem()
    {
        var response = await fixture.Factory.CreateClient().GetAsync(Countries);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
        problem.GetProperty("title").GetString().ShouldBe("You need to sign in");
        response.Headers.WwwAuthenticate.ToString().ShouldStartWith("Bearer");
    }

    [Fact]
    public async Task EndpointWithoutAuthAttributes_StillRequiresSignIn()
    {
        (await fixture.Factory.CreateClient().GetAsync("/api/v1/probes/unannotated"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await fixture.Factory.CreateClientWithRoles("Orders.Read").GetAsync("/api/v1/probes/unannotated"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TamperedToken_Returns401()
    {
        var client = fixture.Factory.CreateClientWithRoles("Orders.Read");
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^4] + "AAAA");

        var response = await client.GetAsync(Countries);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenWithoutARole_Returns403_FriendlyProblem()
    {
        var client = fixture.Factory.CreateClient();
        var token = fixture.Factory.Services.GetRequiredService<OrderManagement.Api.Auth.MockTokenIssuer>()
            .Issue("No Roles", []);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(Countries);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        problem.GetProperty("title").GetString().ShouldBe("You don't have access to this");
    }

    [Theory]
    [InlineData("Orders.Read")]
    [InlineData("Orders.Write")] // Write implies Read
    [InlineData("Orders.Admin")] // Admin implies everything
    public async Task AnyOrdersRole_CanRead(string role)
    {
        var response = await fixture.Factory.CreateClientWithRoles(role).GetAsync(Countries);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DevToken_IssuesAWorkingToken()
    {
        var client = fixture.Factory.CreateClient();

        var tokenResponse = await client.PostAsJsonAsync("/api/v1/dev/token", new { name = "Steve", roles = new[] { "Orders.Read" } });
        tokenResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tokenType").GetString().ShouldBe("Bearer");
        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldBe(["Orders.Read"]);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        (await client.GetAsync(Countries)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DevToken_UnknownRole_IsAValidationError()
    {
        var response = await fixture.Factory.CreateClient()
            .PostAsJsonAsync("/api/v1/dev/token", new { roles = new[] { "Orders.Read", "SuperUser" } });

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.FieldErrors("roles[1]").ShouldBe(["'SuperUser' isn't a role. Use Orders.Read, Orders.Write, Orders.Admin."]);
    }

    [Fact]
    public async Task DevToken_IsNotAvailableOutsideDevelopmentAndTesting()
    {
        using var staging = fixture.Factory.WithWebHostBuilder(b => b.UseEnvironment("Staging"));

        var response = await staging.CreateClient().PostAsJsonAsync("/api/v1/dev/token", new { roles = new[] { "Orders.Admin" } });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
