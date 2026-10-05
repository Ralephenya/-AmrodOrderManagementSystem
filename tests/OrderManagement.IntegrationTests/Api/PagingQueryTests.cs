using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrderManagement.IntegrationTests.Fixtures;

namespace OrderManagement.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class PagingQueryTests(IntegrationTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Factory.CreateClient();

    [Fact]
    public async Task Defaults_ArePage1Size20()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/probes/paged");

        body.GetProperty("page").GetInt32().ShouldBe(1);
        body.GetProperty("pageSize").GetInt32().ShouldBe(20);
        body.GetProperty("sort").GetProperty("field").GetString().ShouldBe("createdAt");
        body.GetProperty("sort").GetProperty("descending").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task PageSizeAbove100_IsRejected()
    {
        var response = await _client.GetAsync("/api/v1/probes/paged?pageSize=101");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.FieldErrors("pageSize").ShouldBe(["Page size must be between 1 and 100."]);
    }

    [Fact]
    public async Task PageBelow1_IsRejected()
    {
        var response = await _client.GetAsync("/api/v1/probes/paged?page=0");

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed"))
            .FieldErrors("page").ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("total", "total", false)]
    [InlineData("-total", "total", true)]
    [InlineData("CreatedAt", "CreatedAt", false)]
    public async Task Sort_AcceptsAllowedFieldsWithOptionalDescendingPrefix(string sort, string field, bool descending)
    {
        var body = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/probes/paged?sort={sort}");

        body.GetProperty("sort").GetProperty("field").GetString().ShouldBe(field);
        body.GetProperty("sort").GetProperty("descending").GetBoolean().ShouldBe(descending);
    }

    [Fact]
    public async Task Sort_ByAnUnlistedColumn_IsRejectedWithGuidance()
    {
        var response = await _client.GetAsync("/api/v1/probes/paged?sort=-customerEmail");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.FieldErrors("sort").ShouldBe(
            ["You can sort by createdAt or total. Add a leading '-' for descending (e.g. -createdAt)."]);
    }

    [Fact]
    public async Task NonNumericPage_IsRejectedInPlainLanguage()
    {
        var response = await _client.GetAsync("/api/v1/probes/paged?page=abc");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.GetProperty("errors").TryGetProperty("page", out _).ShouldBeTrue();
    }
}
