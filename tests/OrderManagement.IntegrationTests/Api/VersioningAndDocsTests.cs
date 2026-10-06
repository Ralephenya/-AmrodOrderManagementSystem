using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrderManagement.IntegrationTests.Fixtures;

namespace OrderManagement.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class VersioningAndDocsTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task VersionedRoute_ReturnsSadcCountries_AndAdvertisesSupportedVersions()
    {
        var response = await fixture.Factory.CreateClientWithRoles("Orders.Read").GetAsync("/api/v1/reference/countries");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("api-supported-versions").ShouldContain("1.0");
        var countries = await response.Content.ReadFromJsonAsync<JsonElement>();
        countries.GetArrayLength().ShouldBe(16);
        var namibia = countries.EnumerateArray().Single(c => c.GetProperty("code").GetString() == "NA");
        namibia.GetProperty("isCommonMonetaryArea").GetBoolean().ShouldBeTrue();
        namibia.GetProperty("currencies").EnumerateArray().Select(c => c.GetProperty("code").GetString())
            .ShouldBe(["NAD", "ZAR"]);
    }

    [Fact]
    public async Task UnversionedRoute_FromTheBrief_IsAnAliasOfV1()
    {
        var client = fixture.Factory.CreateClientWithRoles("Orders.Read");

        var unversioned = await client.GetStringAsync("/api/reference/countries");
        var v1 = await client.GetStringAsync("/api/v1/reference/countries");

        unversioned.ShouldBe(v1);
    }

    [Fact]
    public async Task UnsupportedVersion_ReturnsAProblem()
    {
        var response = await fixture.Factory.CreateClientWithRoles("Orders.Read").GetAsync("/api/v9/reference/countries");

        ((int)response.StatusCode).ShouldBeInRange(400, 404);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task OpenApiDocument_DescribesV1_WithBearerAuth()
    {
        var response = await fixture.Factory.CreateClient().GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("info").GetProperty("version").GetString().ShouldBe("1.0");
        doc.GetProperty("paths").TryGetProperty("/api/v1/reference/countries", out _).ShouldBeTrue();
        doc.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task OpenApiDocument_MarksResponseFieldsRequired_AndNamesQueryParametersInCamelCase()
    {
        // The web app generates its TypeScript types from this document, so these shapes are part of its contract.
        var doc = await fixture.Factory.CreateClient().GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var schemas = doc.GetProperty("components").GetProperty("schemas");

        static string[] Required(JsonElement schema) =>
            schema.TryGetProperty("required", out var required) ? [.. required.EnumerateArray().Select(r => r.GetString()!)] : [];

        var order = Required(schemas.GetProperty("OrderResponse"));
        order.ShouldContain("id");
        order.ShouldContain("status");
        order.ShouldContain("totalAmount");
        order.ShouldContain("lineItems");
        order.ShouldNotContain("allocatedAt"); // nullable: null until stock is allocated
        Required(schemas.GetProperty("OrderSummaryResponsePagedResult")).ShouldContain("totalPages");
        Required(schemas.GetProperty("CreateOrderRequest")).ShouldBeEmpty(); // requests are the validators' business

        var listOrders = doc.GetProperty("paths").GetProperty("/api/v1/orders").GetProperty("get").GetProperty("parameters");
        listOrders.EnumerateArray().Select(p => p.GetProperty("name").GetString())
            .ShouldBe(["customerId", "status", "sort", "page", "pageSize"], ignoreOrder: true);
    }

    [Fact]
    public async Task ScalarReference_IsServed()
    {
        var response = await fixture.Factory.CreateClient().GetAsync("/scalar/v1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Amrod Order Management API");
    }
}
