using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrderManagement.IntegrationTests.Fixtures;

namespace OrderManagement.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class CustomersApiTests(IntegrationTestFixture fixture)
{
    private const string Customers = "/api/v1/customers";

    private readonly HttpClient _writer = fixture.Factory.CreateClientWithRoles("Orders.Write");

    [Fact]
    public async Task Create_Returns201_WithLocation_AndNormalisedFields()
    {
        var email = UniqueEmail();

        var response = await _writer.PostAsJsonAsync(Customers, new
        {
            name = "  Lerato Mokoena  ",
            email = email.ToUpperInvariant(),
            countryCode = "ls",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        response.Headers.Location!.AbsolutePath.ShouldBe($"/api/v1/customers/{id}");
        body.GetProperty("name").GetString().ShouldBe("Lerato Mokoena");
        body.GetProperty("email").GetString().ShouldBe(email);
        body.GetProperty("countryCode").GetString().ShouldBe("LS");
        body.GetProperty("createdAt").GetString().ShouldEndWith("Z");
    }

    [Fact]
    public async Task Create_ThenGetByLocation_ReturnsTheSameCustomer()
    {
        var created = await _writer.PostAsJsonAsync(Customers, NewCustomer());
        var createdBody = await created.Content.ReadAsStringAsync();

        var fetched = await fixture.Factory.CreateClientWithRoles("Orders.Read").GetAsync(created.Headers.Location);

        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await fetched.Content.ReadAsStringAsync()).ShouldBe(createdBody);
    }

    [Fact]
    public async Task Create_ViaTheBriefsUnversionedRoute_Works()
    {
        var response = await _writer.PostAsJsonAsync("/api/customers", NewCustomer());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_InvalidFields_Returns400_WithAMessagePerField()
    {
        var response = await _writer.PostAsJsonAsync(Customers, new { name = "", email = "not-an-email", countryCode = "KE" });

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.FieldErrors("name").ShouldBe(["Please enter the customer's name."]);
        problem.FieldErrors("email").ShouldBe(["Please enter a valid email address, like name@example.co.za."]);
        problem.FieldErrors("countryCode").ShouldBe(
            ["'KE' isn't a SADC country we serve. Use a two-letter code such as ZA, BW or NA."]);
    }

    [Fact]
    public async Task Create_FieldsTooLong_Returns400()
    {
        var response = await _writer.PostAsJsonAsync(Customers, new
        {
            name = new string('N', 201),
            email = new string('e', 320) + "@x.co",
            countryCode = "ZA",
        });

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.FieldErrors("name").ShouldBe(["Name can be at most 200 characters."]);
        problem.FieldErrors("email").ShouldContain("Email can be at most 320 characters.");
    }

    [Fact]
    public async Task Create_DuplicateEmail_IgnoringCase_Returns409()
    {
        var email = UniqueEmail();
        (await _writer.PostAsJsonAsync(Customers, NewCustomer(email))).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await _writer.PostAsJsonAsync(Customers, NewCustomer(email.ToUpperInvariant()));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "email_already_exists");
        problem.GetProperty("detail").GetString().ShouldBe($"A customer with the email address {email} already exists.");
    }

    [Fact]
    public async Task Create_SameEmailConcurrently_ExactlyOneWins()
    {
        var email = UniqueEmail();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => fixture.Factory.CreateClientWithRoles("Orders.Write").PostAsJsonAsync(Customers, NewCustomer(email))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(4);
    }

    [Fact]
    public async Task Create_WithReadOnlyRole_Returns403()
    {
        var response = await fixture.Factory.CreateClientWithRoles("Orders.Read").PostAsJsonAsync(Customers, NewCustomer());

        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task GetById_Unknown_Returns404_WithTheId()
    {
        var id = Guid.NewGuid();

        var response = await fixture.Factory.CreateClientWithRoles("Orders.Read").GetAsync($"{Customers}/{id}");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "not_found");
        problem.GetProperty("detail").GetString().ShouldBe($"We couldn't find a customer with ID {id}.");
    }

    [Fact]
    public async Task List_SearchesNameOrEmailPrefix_Paged_AndSortedByNameByDefault()
    {
        var tag = Tag();
        await Seed($"{tag} Charlie", $"{tag} Alpha", $"{tag} Bravo");
        await SeedWithEmail($"{tag.ToLowerInvariant()}.byemail@example.co.za", "Zulu Email Match");

        var reader = fixture.Factory.CreateClientWithRoles("Orders.Read");
        var page1 = await reader.GetFromJsonAsync<JsonElement>($"{Customers}?search={tag}&pageSize=2");
        var page2 = await reader.GetFromJsonAsync<JsonElement>($"{Customers}?search={tag}&pageSize=2&page=2");

        Names(page1).ShouldBe([$"{tag} Alpha", $"{tag} Bravo"]);
        page1.GetProperty("totalCount").GetInt32().ShouldBe(4);
        page1.GetProperty("totalPages").GetInt32().ShouldBe(2);
        page1.GetProperty("hasNext").GetBoolean().ShouldBeTrue();
        page1.GetProperty("hasPrevious").GetBoolean().ShouldBeFalse();
        Names(page2).ShouldBe([$"{tag} Charlie", "Zulu Email Match"]);
        page2.GetProperty("hasNext").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task List_SortDescendingByCreatedAt()
    {
        var tag = Tag();
        await Seed($"{tag} First");
        await Seed($"{tag} Second");

        var body = await fixture.Factory.CreateClientWithRoles("Orders.Read")
            .GetFromJsonAsync<JsonElement>($"{Customers}?search={tag}&sort=-createdAt");

        Names(body).ShouldBe([$"{tag} Second", $"{tag} First"]);
    }

    [Fact]
    public async Task List_TreatsLikeWildcardsAsLiteralText()
    {
        var tag = Tag();
        await Seed($"{tag} 100% Cotton", $"{tag} 100 Percent");

        var body = await fixture.Factory.CreateClientWithRoles("Orders.Read")
            .GetFromJsonAsync<JsonElement>($"{Customers}?search={Uri.EscapeDataString($"{tag} 100%")}");

        Names(body).ShouldBe([$"{tag} 100% Cotton"]);
    }

    [Fact]
    public async Task List_NoMatches_ReturnsAnEmptyPage()
    {
        var body = await fixture.Factory.CreateClientWithRoles("Orders.Read")
            .GetFromJsonAsync<JsonElement>($"{Customers}?search={Tag()}");

        body.GetProperty("items").GetArrayLength().ShouldBe(0);
        body.GetProperty("totalCount").GetInt32().ShouldBe(0);
        body.GetProperty("totalPages").GetInt32().ShouldBe(0);
    }

    [Theory]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("page=0", "page")]
    [InlineData("sort=email", "sort")]
    public async Task List_InvalidQuery_Returns400(string queryString, string field)
    {
        var response = await fixture.Factory.CreateClientWithRoles("Orders.Read").GetAsync($"{Customers}?{queryString}");

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed"))
            .FieldErrors(field).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task List_SearchTooLong_Returns400()
    {
        var response = await fixture.Factory.CreateClientWithRoles("Orders.Read").GetAsync($"{Customers}?search={new string('a', 101)}");

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed"))
            .FieldErrors("search").ShouldBe(["Search text can be at most 100 characters."]);
    }

    private static string Tag() => "T" + Guid.NewGuid().ToString("N")[..10];

    private static string UniqueEmail() => $"customer.{Guid.NewGuid():N}@example.co.za";

    private static object NewCustomer(string? email = null) =>
        new { name = "Thandi Nkosi", email = email ?? UniqueEmail(), countryCode = "ZA" };

    private async Task Seed(params string[] names)
    {
        foreach (var name in names)
        {
            await SeedWithEmail(UniqueEmail(), name);
        }
    }

    private async Task SeedWithEmail(string email, string name)
    {
        var response = await _writer.PostAsJsonAsync(Customers, new { name, email, countryCode = "BW" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static string[] Names(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("name").GetString()!)];
}
