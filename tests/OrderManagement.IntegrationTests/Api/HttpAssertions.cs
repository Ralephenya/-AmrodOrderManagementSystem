using System.Net;
using System.Text.Json;

namespace OrderManagement.IntegrationTests.Api;

internal static class HttpAssertions
{
    /// <summary>Asserts a ProblemDetails response with the expected status and stable code, and returns its JSON.</summary>
    public static async Task<JsonElement> ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var json = JsonDocument.Parse(body).RootElement.Clone();
        json.GetProperty("status").GetInt32().ShouldBe((int)status);
        json.GetProperty("code").GetString().ShouldBe(code);
        json.GetProperty("type").GetString().ShouldNotBeNullOrWhiteSpace();
        json.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        json.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        json.GetProperty("correlationId").GetString().ShouldNotBeNullOrWhiteSpace();
        return json;
    }

    public static string[] FieldErrors(this JsonElement problem, string field) =>
        [.. problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!)];
}
