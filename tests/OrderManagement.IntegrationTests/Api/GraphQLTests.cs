using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using OrderManagement.Domain.Customers;
using OrderManagement.Domain.Orders;
using OrderManagement.IntegrationTests.Fixtures;

namespace OrderManagement.IntegrationTests.Api;

/// <summary>The read-only GraphQL endpoint (/graphql): orders by customer with nested line items and filters.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class GraphQLTests(IntegrationTestFixture fixture) : IAsyncLifetime
{
    private const string OrdersQuery = """
        query Orders($customerId: UUID!, $first: Int, $after: String, $where: OrderFilterInput, $order: [OrderSortInput!]) {
          ordersByCustomer(customerId: $customerId, first: $first, after: $after, where: $where, order: $order) {
            totalCount
            pageInfo { hasNextPage endCursor }
            nodes {
              id customerId status currencyCode totalAmount createdAt
              lineItems { productSku quantity unitPrice lineTotal }
            }
          }
        }
        """;

    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static Seeded? _seeded;

    private readonly HttpClient _reader = fixture.Factory.CreateClientWithRoles("Orders.Read");

    private Seeded Data => _seeded!;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            _seeded ??= await SeedAsync();
        }
        finally
        {
            SeedLock.Release();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // One customer with three orders a day apart, plus another customer's order that must never appear.
    private async Task<Seeded> SeedAsync()
    {
        await using var db = fixture.CreateDbContext();
        var customer = Customer.Create("Gaborone Office Supplies", $"gql.{Guid.NewGuid():N}@example.co.bw", "BW", TestData.Clock()).Value;
        var other = Customer.Create("Francistown Stationers", $"gql.{Guid.NewGuid():N}@example.co.bw", "BW", TestData.Clock()).Value;
        db.Customers.AddRange(customer, other);

        var oldest = Seed(db, customer, daysAgo: 3, OrderStatus.Paid, ("PEN-01", 10, 2.50m), ("MUG-02", 2, 45m)); // 115.00
        var middle = Seed(db, customer, daysAgo: 2, OrderStatus.Pending, ("CAP-03", 1, 80m));                    // 80.00
        var newest = Seed(db, customer, daysAgo: 1, OrderStatus.Paid, ("BAG-04", 3, 150m));                      // 450.00
        Seed(db, other, daysAgo: 1, OrderStatus.Paid, ("PEN-01", 1, 2.50m));
        await db.SaveChangesAsync();

        return new Seeded(customer.Id, oldest.Id, middle.Id, newest.Id);
    }

    private sealed record Seeded(Guid CustomerId, Guid Oldest, Guid Middle, Guid Newest);

    [Fact]
    public async Task OrdersByCustomer_ReturnsOnlyThatCustomersOrders_NewestFirst_WithLineItems()
    {
        var result = await QueryAsync(_reader, new { customerId = Data.CustomerId });

        var connection = result.Data("ordersByCustomer");
        connection.GetProperty("totalCount").GetInt32().ShouldBe(3);
        var nodes = connection.GetProperty("nodes").EnumerateArray().ToList();
        nodes.Select(n => n.GetProperty("id").GetGuid()).ShouldBe([Data.Newest, Data.Middle, Data.Oldest]);
        nodes.ShouldAllBe(n => n.GetProperty("customerId").GetGuid() == Data.CustomerId);

        var oldest = nodes[2];
        oldest.GetProperty("status").GetString().ShouldBe("PAID");
        oldest.GetProperty("currencyCode").GetString().ShouldBe("BWP");
        oldest.GetProperty("totalAmount").GetDecimal().ShouldBe(115m);
        var lines = oldest.GetProperty("lineItems").EnumerateArray().ToList();
        lines.Select(l => l.GetProperty("productSku").GetString()).ShouldBe(["MUG-02", "PEN-01"]); // by SKU
        lines[1].GetProperty("lineTotal").GetRawText().ShouldBe("25.00"); // 10 × 2.50, with BWP's two decimals
    }

    [Fact]
    public async Task LineTotal_IsComputed_EvenWhenQuantityAndPriceArentRequested()
    {
        var response = await _reader.PostAsJsonAsync("/graphql", new
        {
            query = "query ($c: UUID!) { ordersByCustomer(customerId: $c) { nodes { id lineItems { productSku lineTotal } } } }",
            variables = new { c = Data.CustomerId },
        });
        var result = await GraphQLResult.ReadAsync(response);

        var newest = result.Data("ordersByCustomer").GetProperty("nodes")[0];
        newest.GetProperty("lineItems")[0].GetProperty("lineTotal").GetRawText().ShouldBe("450.00"); // 3 × 150.00
    }

    [Fact]
    public async Task Filters_Sorting_AndCursorPaging_ComposeOnTheServer()
    {
        var variables = new
        {
            customerId = Data.CustomerId,
            first = 1,
            where = new { status = new { eq = "PAID" } },
            order = new[] { new { totalAmount = "DESC" } },
        };

        var firstPage = (await QueryAsync(_reader, variables)).Data("ordersByCustomer");
        firstPage.GetProperty("totalCount").GetInt32().ShouldBe(2); // the Pending order is filtered out
        firstPage.GetProperty("nodes")[0].GetProperty("id").GetGuid().ShouldBe(Data.Newest); // 450 before 115
        firstPage.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().ShouldBeTrue();

        var cursor = firstPage.GetProperty("pageInfo").GetProperty("endCursor").GetString();
        var secondPage = (await QueryAsync(_reader, new { variables.customerId, variables.first, variables.where, variables.order, after = cursor }))
            .Data("ordersByCustomer");
        secondPage.GetProperty("nodes")[0].GetProperty("id").GetGuid().ShouldBe(Data.Oldest);
        secondPage.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task OrdersAndTheirLineItems_LoadInOneSqlQuery_NoNPlusOne()
    {
        using var recorder = SqlCommandRecorder.Start();

        var result = await QueryAsync(_reader, new { customerId = Data.CustomerId });

        result.Data("ordersByCustomer").GetProperty("nodes").GetArrayLength().ShouldBe(3);
        // totalCount is its own COUNT query; orders and lines come from one query with a join, not one per order.
        recorder.Commands.Count(c => c.Contains("[OrderLineItems]", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task AClientSort_StillEndsWithIdInSql_SoTiesPageDeterministically()
    {
        using var recorder = SqlCommandRecorder.Start();

        var result = await QueryAsync(_reader, new { customerId = Data.CustomerId, order = new[] { new { totalAmount = "DESC" } } });

        result.Data("ordersByCustomer").GetProperty("nodes").GetArrayLength().ShouldBe(3);
        var page = recorder.Commands.Single(c => c.Contains("[OrderLineItems]", StringComparison.Ordinal));
        page.ShouldMatch(@"ORDER BY \[\w+\]\.\[TotalAmount\] DESC, \[\w+\]\.\[Id\] DESC");
    }

    [Fact]
    public async Task Errors_CarryTheRequestsCorrelationId_LikeRestProblemDetails()
    {
        var anonymous = fixture.Factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Correlation-ID", "gql-correlation-123");

        var response = await anonymous.PostAsJsonAsync("/graphql", new
        {
            query = "query ($c: UUID!) { ordersByCustomer(customerId: $c) { totalCount } }",
            variables = new { c = Data.CustomerId },
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var error = body.GetProperty("errors")[0].GetProperty("extensions");
        error.GetProperty("code").GetString().ShouldBe("AUTH_NOT_AUTHENTICATED");
        error.GetProperty("correlationId").GetString().ShouldBe("gql-correlation-123");
    }

    [Fact]
    public async Task WithoutSigningIn_ReturnsAnAuthorizationError_AndNoOrders()
    {
        var result = await QueryAsync(fixture.Factory.CreateClient(), new { customerId = Data.CustomerId });

        result.ErrorCodes.ShouldContain("AUTH_NOT_AUTHENTICATED");
        result.HasData("ordersByCustomer").ShouldBeFalse();
    }

    [Fact]
    public async Task PageSizesAbove100_AreRejected()
    {
        var result = await QueryAsync(_reader, new { customerId = Data.CustomerId, first = 101 });

        result.ErrorCodes.ShouldContain("HC0051"); // Hot Chocolate: "the maximum allowed items per page were exceeded"
        result.HasData("ordersByCustomer").ShouldBeFalse();
    }

    [Fact]
    public async Task ItIsReadOnly_MutationsAreNotPartOfTheSchema()
    {
        var response = await _reader.PostAsJsonAsync("/graphql", new { query = "mutation { anything }" });
        var result = await GraphQLResult.ReadAsync(response);

        result.Errors.ShouldNotBeEmpty();
        result.Errors.ShouldContain(e => e.Contains("mutation", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<GraphQLResult> QueryAsync(HttpClient client, object variables)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query = OrdersQuery, variables });
        return await GraphQLResult.ReadAsync(response);
    }

    private static Order Seed(
        Infrastructure.Persistence.AppDbContext db, Customer customer, int daysAgo, OrderStatus status,
        params (string Sku, int Quantity, decimal UnitPrice)[] lines)
    {
        var clock = new FakeTimeProvider(TestData.Now.AddDays(-daysAgo));
        var order = Order.Create(customer, "BWP", [.. lines.Select(l => new NewOrderLine(l.Sku, l.Quantity, l.UnitPrice))], clock).Value;
        if (status == OrderStatus.Paid)
        {
            order.TransitionTo(OrderStatus.Paid);
        }

        db.Orders.Add(order);
        return order;
    }

    private sealed class GraphQLResult(JsonElement root)
    {
        public static async Task<GraphQLResult> ReadAsync(HttpResponseMessage response) =>
            new(await response.Content.ReadFromJsonAsync<JsonElement>());

        public IReadOnlyList<string> Errors =>
            root.TryGetProperty("errors", out var errors) ? [.. errors.EnumerateArray().Select(e => e.GetProperty("message").GetString()!)] : [];

        public IReadOnlyList<string> ErrorCodes =>
            root.TryGetProperty("errors", out var errors)
                ? [.. errors.EnumerateArray()
                    .Where(e => e.TryGetProperty("extensions", out var x) && x.TryGetProperty("code", out _))
                    .Select(e => e.GetProperty("extensions").GetProperty("code").GetString()!)]
                : [];

        public bool HasData(string field) =>
            root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null;

        public JsonElement Data(string field)
        {
            Errors.ShouldBeEmpty();
            return root.GetProperty("data").GetProperty(field);
        }
    }

    /// <summary>Records the SQL EF Core executes in this process while it's alive (the API runs in-process).</summary>
    private sealed class SqlCommandRecorder : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly ConcurrentQueue<string> _commands = new();
        private readonly List<IDisposable> _subscriptions = [];

        public IReadOnlyCollection<string> Commands => _commands;

        public static SqlCommandRecorder Start()
        {
            var recorder = new SqlCommandRecorder();
            recorder._subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(recorder));
            return recorder;
        }

        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == DbLoggerCategory.Name)
            {
                lock (_subscriptions)
                {
                    _subscriptions.Add(value.Subscribe(this));
                }
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key == RelationalEventId.CommandExecuted.Name && value.Value is CommandExecutedEventData executed)
            {
                _commands.Enqueue(executed.Command.CommandText);
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void Dispose()
        {
            lock (_subscriptions)
            {
                _subscriptions.ForEach(s => s.Dispose());
            }
        }
    }
}
