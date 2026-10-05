using System.Net;
using System.Net.Http.Json;
using OrderManagement.IntegrationTests.Api.Probes;
using OrderManagement.IntegrationTests.Fixtures;

namespace OrderManagement.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class ErrorHandlingTests(IntegrationTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Factory.CreateClient();

    [Fact]
    public async Task UnhandledException_Returns500_WithFriendlyMessage_AndNoInternalDetails()
    {
        var response = await _client.GetAsync("/api/v1/probes/boom");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "server_error");
        problem.GetProperty("title").GetString().ShouldBe("Something went wrong on our side");
        var raw = problem.GetRawText();
        raw.ShouldNotContain("hunter2");
        raw.ShouldNotContain("InvalidOperationException");
        raw.ShouldNotContain("   at "); // no stack trace
    }

    [Fact]
    public async Task ConcurrencyException_Returns409ConcurrencyConflict()
    {
        var response = await _client.GetAsync("/api/v1/probes/concurrency");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");
        problem.GetProperty("detail").GetString().ShouldBe("Refresh to see the latest version, then try again.");
    }

    [Fact]
    public async Task DomainNotFound_Returns404_WithTheDomainMessage()
    {
        var response = await _client.GetAsync("/api/v1/probes/domain-error/not-found");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "not_found");
        problem.GetProperty("detail").GetString().ShouldBe("We couldn't find an order with ID 42.");
    }

    [Fact]
    public async Task DomainConflict_Returns409_WithASpecificCode()
    {
        var response = await _client.GetAsync("/api/v1/probes/domain-error/conflict");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "invalid_status_transition");
        problem.GetProperty("detail").GetString().ShouldBe("A Fulfilled order can't be changed any more.");
    }

    [Fact]
    public async Task DomainValidationErrors_Return400_KeyedByField()
    {
        var response = await _client.GetAsync("/api/v1/probes/domain-error/validation");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.GetProperty("title").GetString().ShouldBe("Some details need fixing");
        problem.FieldErrors("currencyCode").ShouldBe(["Customers in South Africa can only order in ZAR, not BWP."]);
        problem.FieldErrors("lineItems[0].quantity").ShouldBe(["Line 1: quantity must be at least 1."]);
    }

    [Fact]
    public async Task UnexpectedDomainError_DoesNotLeakItsDescription()
    {
        var response = await _client.GetAsync("/api/v1/probes/domain-error/other");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "server_error");
        problem.GetRawText().ShouldNotContain(ProbesController.SecretMessage);
    }

    [Fact]
    public async Task FluentValidationFailures_Return400_WithJsonFieldPaths()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/probes/validate", new
        {
            name = "",
            lineItems = new[] { new { productSku = "A", quantity = 1 }, new { productSku = "B", quantity = 0 } },
        });

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.FieldErrors("name").ShouldBe(["Please enter a name."]);
        problem.FieldErrors("lineItems[1].quantity").ShouldBe(["Quantity must be at least 1."]);
    }

    [Fact]
    public async Task MalformedJson_Returns400_InPlainLanguage()
    {
        using var content = new StringContent("""{ "name": "x", "lineItems": [ { "quantity": "lots" } ] }""",
            System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/v1/probes/validate", content);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.FieldErrors("lineItems[0].quantity").ShouldBe(["This value isn't in the expected format."]);
        problem.GetRawText().ShouldNotContain("System.Int32");
    }

    [Fact]
    public async Task MissingBody_Returns400_InPlainLanguage()
    {
        using var content = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/v1/probes/validate", content);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.GetProperty("errors").EnumerateObject().SelectMany(p => p.Value.EnumerateArray())
            .Select(e => e.GetString()).ShouldContain("The request body is missing or isn't valid JSON.");
    }

    [Fact]
    public async Task UnknownRoute_Returns404ProblemDetails()
    {
        var client = fixture.Factory.CreateClientWithRoles("Orders.Read");

        var response = await client.GetAsync("/api/v1/does-not-exist");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "not_found");
        problem.GetProperty("title").GetString().ShouldBe("We couldn't find that");
    }
}
