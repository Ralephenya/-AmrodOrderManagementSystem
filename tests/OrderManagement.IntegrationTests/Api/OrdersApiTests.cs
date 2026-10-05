using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrderManagement.IntegrationTests.Fixtures;

namespace OrderManagement.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class OrdersApiTests(IntegrationTestFixture fixture)
{
    private const string Orders = "/api/v1/orders";

    private readonly HttpClient _writer = fixture.Factory.CreateClientWithRoles("Orders.Write");

    // ---------- Create ----------

    [Fact]
    public async Task Create_Returns201_ComputesTheTotal_AndIgnoresAnyClientTotal()
    {
        var customerId = await CreateCustomerAsync("ZA");

        var response = await _writer.PostAsJsonAsync(Orders, new
        {
            customerId,
            currencyCode = "zar",
            totalAmount = 1.00m, // not part of the contract: the server computes the total
            lineItems = new object[]
            {
                new { productSku = "pen-001", quantity = 3, unitPrice = 19.99m },
                new { productSku = "MUG-002", quantity = 2, unitPrice = 85.50m },
            },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var order = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = order.GetProperty("id").GetGuid();
        response.Headers.Location!.AbsolutePath.ShouldBe($"/api/v1/orders/{id}");
        response.Headers.ETag.ShouldNotBeNull();
        order.GetProperty("totalAmount").GetDecimal().ShouldBe(230.97m);
        order.GetProperty("currencyCode").GetString().ShouldBe("ZAR");
        order.GetProperty("status").GetString().ShouldBe("Pending");
        Strings(order, "allowedTransitions").ShouldBe(["Paid", "Cancelled"]);
        order.GetProperty("lineItems").EnumerateArray().Select(l => (l.GetProperty("productSku").GetString(), l.GetProperty("lineTotal").GetDecimal()))
            .ShouldBe([("MUG-002", 171.00m), ("PEN-001", 59.97m)]);
    }

    [Fact]
    public async Task Create_CurrencyNotAllowedForTheCustomersCountry_Returns400OnCurrencyCode()
    {
        var customerId = await CreateCustomerAsync("BW");

        var response = await _writer.PostAsJsonAsync(Orders, NewOrder(customerId, "ZAR"));

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed"))
            .FieldErrors("currencyCode").ShouldBe(["Customers in Botswana can only order in BWP, not ZAR."]);
    }

    [Theory]
    [InlineData("NA", "ZAR")] // Common Monetary Area: rand is legal tender
    [InlineData("NA", "NAD")]
    [InlineData("ZW", "USD")]
    [InlineData("ZW", "ZWL")]
    public async Task Create_PermittedCurrencies_AreAccepted(string country, string currency)
    {
        var customerId = await CreateCustomerAsync(country);

        (await _writer.PostAsJsonAsync(Orders, NewOrder(customerId, currency))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_UnknownCustomer_Returns400OnCustomerId()
    {
        var customerId = Guid.NewGuid();

        var response = await _writer.PostAsJsonAsync(Orders, NewOrder(customerId, "ZAR"));

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed"))
            .FieldErrors("customerId").ShouldBe([$"We couldn't find a customer with ID {customerId}."]);
    }

    [Fact]
    public async Task Create_InvalidShape_ReportsEveryField()
    {
        var response = await _writer.PostAsJsonAsync(Orders, new
        {
            customerId = (Guid?)null,
            currencyCode = "",
            lineItems = new[] { new { productSku = "", quantity = 0, unitPrice = -1m } },
        });

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.FieldErrors("customerId").ShouldBe(["Please choose the customer this order is for."]);
        problem.FieldErrors("currencyCode").ShouldContain("Please choose a currency, such as ZAR.");
        problem.FieldErrors("lineItems[0].productSku").ShouldBe(["Please enter a product SKU."]);
        problem.FieldErrors("lineItems[0].quantity").ShouldBe(["Quantity must be at least 1."]);
        problem.FieldErrors("lineItems[0].unitPrice").ShouldBe(["Unit price can't be negative."]);
    }

    [Fact]
    public async Task Create_NoLineItems_Returns400()
    {
        var response = await _writer.PostAsJsonAsync(Orders, new { customerId = Guid.NewGuid(), currencyCode = "ZAR", lineItems = Array.Empty<object>() });

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed"))
            .FieldErrors("lineItems").ShouldBe(["An order needs at least one line item."]);
    }

    [Fact]
    public async Task Create_PriceMorePreciseThanTheCurrency_Returns400()
    {
        var customerId = await CreateCustomerAsync("KM");

        var response = await _writer.PostAsJsonAsync(Orders, NewOrder(customerId, "KMF", unitPrice: 500.5m));

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed"))
            .FieldErrors("lineItems[0].unitPrice").ShouldBe(["Line 1: KMF prices can't have decimals."]);
    }

    [Fact]
    public async Task Create_DuplicateSku_Returns400()
    {
        var customerId = await CreateCustomerAsync("ZA");

        var response = await _writer.PostAsJsonAsync(Orders, new
        {
            customerId,
            currencyCode = "ZAR",
            lineItems = new[] { new { productSku = "PEN", quantity = 1, unitPrice = 1m }, new { productSku = "pen", quantity = 2, unitPrice = 1m } },
        });

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed"))
            .FieldErrors("lineItems").Single().ShouldStartWith("SKU PEN appears on more than one line.");
    }

    [Fact]
    public async Task Create_WithReadOnlyRole_Returns403()
    {
        var response = await fixture.Factory.CreateClientWithRoles("Orders.Read").PostAsJsonAsync(Orders, NewOrder(Guid.NewGuid(), "ZAR"));

        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    // ---------- Get + ETag ----------

    [Fact]
    public async Task Get_ReturnsLineItems_WithETag_AndRevalidatingCacheControl()
    {
        var (id, createdEtag) = await CreateOrderAsync();

        var response = await Reader().GetAsync($"{Orders}/{id}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.ShouldBe(createdEtag);
        response.Headers.CacheControl!.Private.ShouldBeTrue();
        response.Headers.CacheControl.NoCache.ShouldBeTrue();
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lineItems").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Get_IfNoneMatchCurrentETag_Returns304WithoutABody()
    {
        var (id, etag) = await CreateOrderAsync();

        var response = await GetWith(id, "If-None-Match", etag);

        response.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();
        response.Headers.ETag!.Tag.ShouldBe(etag);
    }

    [Fact]
    public async Task Get_IfNoneMatch_AcceptsWeakFormAndLists()
    {
        var (id, etag) = await CreateOrderAsync();

        (await GetWith(id, "If-None-Match", $"W/{etag}")).StatusCode.ShouldBe(HttpStatusCode.NotModified);
        (await GetWith(id, "If-None-Match", $"\"stale\", {etag}")).StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Get_AfterAChange_TheOldETagNoLongerMatches()
    {
        var (id, etag) = await CreateOrderAsync();
        (await PutStatus(id, "Paid")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await GetWith(id, "If-None-Match", etag);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.ShouldNotBe(etag);
    }

    [Fact]
    public async Task Get_Unknown_Returns404()
    {
        var id = Guid.NewGuid();

        var problem = await (await Reader().GetAsync($"{Orders}/{id}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "not_found");

        problem.GetProperty("detail").GetString().ShouldBe($"We couldn't find an order with ID {id}.");
    }

    // ---------- List ----------

    [Fact]
    public async Task List_FiltersByCustomerAndStatus_AndSortsByTotal()
    {
        var customerId = await CreateCustomerAsync("ZA", "List Customer");
        var small = await CreateOrderAsync(customerId, unitPrice: 10m);
        var large = await CreateOrderAsync(customerId, unitPrice: 300m);
        var medium = await CreateOrderAsync(customerId, unitPrice: 50m);
        (await PutStatus(medium.Id, "Cancelled")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var pending = await Reader().GetFromJsonAsync<JsonElement>($"{Orders}?customerId={customerId}&status=pending&sort=-total");
        var all = await Reader().GetFromJsonAsync<JsonElement>($"{Orders}?customerId={customerId}&sort=total");

        Ids(pending).ShouldBe([large.Id, small.Id]);
        pending.GetProperty("totalCount").GetInt32().ShouldBe(2);
        pending.GetProperty("items")[0].GetProperty("customerName").GetString().ShouldBe("List Customer");
        Ids(all).ShouldBe([small.Id, medium.Id, large.Id]);
    }

    [Fact]
    public async Task List_DefaultsToNewestFirst_AndPages()
    {
        var customerId = await CreateCustomerAsync("ZA");
        var first = await CreateOrderAsync(customerId);
        var second = await CreateOrderAsync(customerId);
        var third = await CreateOrderAsync(customerId);

        var page1 = await Reader().GetFromJsonAsync<JsonElement>($"{Orders}?customerId={customerId}&pageSize=2");
        var page2 = await Reader().GetFromJsonAsync<JsonElement>($"{Orders}?customerId={customerId}&pageSize=2&page=2");

        Ids(page1).ShouldBe([third.Id, second.Id]);
        Ids(page2).ShouldBe([first.Id]);
        page1.GetProperty("totalPages").GetInt32().ShouldBe(2);
    }

    [Theory]
    [InlineData("status=Shipped", "status", "Status must be one of Pending, Paid, Fulfilled, Cancelled.")]
    [InlineData("status=2", "status", "Status must be one of Pending, Paid, Fulfilled, Cancelled.")]
    [InlineData("sort=customerName", "sort", "You can sort by createdAt or total. Add a leading '-' for descending (e.g. -createdAt).")]
    [InlineData("pageSize=101", "pageSize", "Page size must be between 1 and 100.")]
    public async Task List_InvalidQuery_Returns400(string queryString, string field, string message)
    {
        var response = await Reader().GetAsync($"{Orders}?{queryString}");

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed")).FieldErrors(field).ShouldBe([message]);
    }

    // ---------- Status changes + idempotency ----------

    [Fact]
    public async Task ChangeStatus_WithoutIdempotencyKey_Returns400()
    {
        var (id, _) = await CreateOrderAsync();

        var response = await _writer.PutAsJsonAsync($"{Orders}/{id}/status", new { status = "Paid" });

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed"))
            .FieldErrors("Idempotency-Key").Single().ShouldStartWith("Send an Idempotency-Key header");
    }

    [Fact]
    public async Task ChangeStatus_PendingToPaid_Returns200_WithTheNextAllowedTransitions_AndANewETag()
    {
        var (id, etag) = await CreateOrderAsync();

        var response = await PutStatus(id, "paid");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var order = await response.Content.ReadFromJsonAsync<JsonElement>();
        order.GetProperty("status").GetString().ShouldBe("Paid");
        Strings(order, "allowedTransitions").ShouldBe(["Fulfilled", "Cancelled"]);
        response.Headers.ETag!.Tag.ShouldNotBe(etag);
        response.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
    }

    [Fact]
    public async Task ChangeStatus_RetryWithTheSameKey_ReplaysTheOriginalResponse_AndAppliesOnce()
    {
        var (id, _) = await CreateOrderAsync();
        var key = Guid.NewGuid().ToString();

        var first = await PutStatus(id, "Paid", key);
        var retry = await PutStatus(id, "Paid", key);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        // Without idempotency the retry would be "This order is already Paid." (409).
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        retry.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        (await retry.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ChangeStatus_SameKeyForADifferentChange_Returns422()
    {
        var (id, _) = await CreateOrderAsync();
        var key = Guid.NewGuid().ToString();
        (await PutStatus(id, "Paid", key)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await PutStatus(id, "Cancelled", key);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "idempotency_key_reused");
        problem.GetProperty("detail").GetString()!.ShouldContain("already used for a different request");
    }

    [Fact]
    public async Task ChangeStatus_SameKeyForADifferentOrder_Returns422()
    {
        var (first, _) = await CreateOrderAsync();
        var (second, _) = await CreateOrderAsync();
        var key = Guid.NewGuid().ToString();
        (await PutStatus(first, "Paid", key)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await (await PutStatus(second, "Paid", key)).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "idempotency_key_reused");
    }

    [Fact]
    public async Task ChangeStatus_KeysAreScopedPerUser()
    {
        var (first, _) = await CreateOrderAsync();
        var (second, _) = await CreateOrderAsync();
        var key = Guid.NewGuid().ToString();

        var alice = await PutStatus(first, "Paid", key, client: fixture.Factory.CreateClientAs("Alice", "Orders.Write"));
        var bob = await PutStatus(second, "Paid", key, client: fixture.Factory.CreateClientAs("Bob", "Orders.Write"));

        alice.StatusCode.ShouldBe(HttpStatusCode.OK);
        bob.StatusCode.ShouldBe(HttpStatusCode.OK);
        bob.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
    }

    [Fact]
    public async Task ChangeStatus_InvalidTransition_Returns409_AndIsReplayedToo()
    {
        var (id, _) = await CreateOrderAsync();
        var key = Guid.NewGuid().ToString();

        var first = await PutStatus(id, "Fulfilled", key);
        var retry = await PutStatus(id, "Fulfilled", key);

        var problem = await first.ShouldBeProblemAsync(HttpStatusCode.Conflict, "invalid_status_transition");
        problem.GetProperty("detail").GetString().ShouldBe(
            "A Pending order can't be moved to Fulfilled. It can only be moved to Paid or Cancelled.");
        await retry.ShouldBeProblemAsync(HttpStatusCode.Conflict, "invalid_status_transition");
        retry.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
    }

    [Fact]
    public async Task ChangeStatus_PaidToFulfilled_BeforeAllocation_Returns409NotAllocated()
    {
        var (id, _) = await CreateOrderAsync();
        (await PutStatus(id, "Paid")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await (await PutStatus(id, "Fulfilled")).ShouldBeProblemAsync(HttpStatusCode.Conflict, "not_allocated");
    }

    [Fact]
    public async Task ChangeStatus_TerminalOrder_Returns409()
    {
        var (id, _) = await CreateOrderAsync();
        (await PutStatus(id, "Cancelled")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var problem = await (await PutStatus(id, "Paid")).ShouldBeProblemAsync(HttpStatusCode.Conflict, "invalid_status_transition");

        problem.GetProperty("detail").GetString().ShouldBe("A Cancelled order can't be changed any more.");
    }

    [Fact]
    public async Task ChangeStatus_IfMatchStale_Returns412_AndIfMatchCurrent_Succeeds()
    {
        var (id, original) = await CreateOrderAsync();
        var paid = await PutStatus(id, "Paid");
        var current = paid.Headers.ETag!.Tag;

        var stale = await PutStatus(id, "Cancelled", ifMatch: original);
        var fresh = await PutStatus(id, "Cancelled", ifMatch: current);

        var problem = await stale.ShouldBeProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        problem.GetProperty("detail").GetString()!.ShouldContain("changed since you loaded it");
        fresh.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangeStatus_InvalidStatusValue_Returns400()
    {
        var (id, _) = await CreateOrderAsync();

        (await (await PutStatus(id, "Shipped")).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed"))
            .FieldErrors("status").ShouldBe(["Status must be one of Pending, Paid, Fulfilled, Cancelled."]);
    }

    [Fact]
    public async Task ChangeStatus_UnknownOrder_Returns404()
    {
        await (await PutStatus(Guid.NewGuid(), "Paid")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task ChangeStatus_ConcurrentRetriesOfOneRequest_AllSucceed_AndApplyOnce()
    {
        var (id, _) = await CreateOrderAsync();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            PutStatus(id, "Paid", key, client: fixture.Factory.CreateClientWithRoles("Orders.Write"))));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        responses.Count(r => !r.Headers.Contains("Idempotent-Replayed")).ShouldBe(1);
    }

    [Fact]
    public async Task ChangeStatus_ViaTheBriefsUnversionedRoute_Works()
    {
        var (id, _) = await CreateOrderAsync();

        (await PutStatus(id, "Paid", path: $"/api/orders/{id}/status")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- helpers ----------

    private HttpClient Reader() => fixture.Factory.CreateClientWithRoles("Orders.Read");

    private async Task<Guid> CreateCustomerAsync(string country, string name = "Order Test Customer")
    {
        var response = await _writer.PostAsJsonAsync("/api/v1/customers",
            new { name, email = $"orders.{Guid.NewGuid():N}@example.co.za", countryCode = country });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<(Guid Id, string ETag)> CreateOrderAsync(Guid? customerId = null, decimal unitPrice = 10m)
    {
        var response = await _writer.PostAsJsonAsync(Orders, NewOrder(customerId ?? await CreateCustomerAsync("ZA"), "ZAR", unitPrice));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return ((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(), response.Headers.ETag!.Tag);
    }

    private static object NewOrder(Guid customerId, string currency, decimal unitPrice = 10m) => new
    {
        customerId,
        currencyCode = currency,
        lineItems = new[] { new { productSku = "PEN-001", quantity = 1, unitPrice } },
    };

    private Task<HttpResponseMessage> PutStatus(
        Guid id, string status, string? key = null, string? ifMatch = null, HttpClient? client = null, string? path = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, path ?? $"{Orders}/{id}/status")
        {
            Content = JsonContent.Create(new { status }),
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return (client ?? _writer).SendAsync(request);
    }

    private Task<HttpResponseMessage> GetWith(Guid id, string header, string value)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{Orders}/{id}");
        request.Headers.TryAddWithoutValidation(header, value);
        return Reader().SendAsync(request);
    }

    private static Guid[] Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("id").GetGuid())];

    private static string[] Strings(JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray().Select(s => s.GetString()!)];
}
