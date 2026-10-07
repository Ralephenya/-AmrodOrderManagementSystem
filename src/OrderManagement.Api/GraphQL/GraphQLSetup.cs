using OrderManagement.Infrastructure.Persistence;

namespace OrderManagement.Api.GraphQL;

/// <summary>
/// A read-only GraphQL endpoint at <c>/graphql</c>, next to the REST API. Queries only: there is no mutation type, so
/// every write still goes through REST with its validation, idempotency keys and concurrency checks.
/// </summary>
internal static class GraphQLSetup
{
    public const string Path = "/graphql";

    // Deep enough for orders → lineItems inside a connection (nodes/edges), shallow enough to stop abusive nesting.
    private const int MaxExecutionDepth = 8;

    // Hot Chocolate scores every query before running it and rejects it above this. The default (1,000) is below a
    // legitimate one: a full page of 100 filtered, sorted orders with their line items scores about 4,000, because
    // filtering and sorting are weighted by the worst-case page size. 10,000 leaves headroom for that while still
    // refusing queries built to be expensive.
    private const double MaxFieldCost = 10_000;

    /// <summary>
    /// The schema, the Nitro IDE, GET queries and detailed errors are for developers. Elsewhere they would hand an
    /// attacker a map, and GET would put customer ids and query text in URLs, where proxies and logs keep them.
    /// </summary>
    public static bool DeveloperFeaturesEnabled(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    public static IServiceCollection AddOrdersGraphQL(this IServiceCollection services, IHostEnvironment environment)
    {
        var developerFeatures = DeveloperFeaturesEnabled(environment);

        services.AddGraphQLServer()
            .AddAuthorization() // [Authorize(Policy = ...)] uses the API's own policies: Orders.Read etc.
            .AddQueryType<OrderQueries>()
            .AddType<OrderLineNodeType>()
            .RegisterDbContextFactory<AppDbContext>()
            .AddErrorFilter(sp =>
            {
                var app = sp.GetRootServiceProvider();
                return new GraphQLErrorFilter(
                    app.GetRequiredService<IHttpContextAccessor>(), app.GetRequiredService<ILogger<GraphQLErrorFilter>>());
            })
            .AddProjections()
            .AddFiltering()
            .AddSorting()
            .AddMaxExecutionDepthRule(MaxExecutionDepth, skipIntrospectionFields: true)
            .ModifyCostOptions(options => options.MaxFieldCost = MaxFieldCost)
            .DisableIntrospection(!developerFeatures)
            .ModifyRequestOptions(options => options.IncludeExceptionDetails = environment.IsDevelopment())
            .ModifyServerOptions(options =>
            {
                options.Tool.Enable = developerFeatures;
                options.EnableSchemaRequests = developerFeatures;
                options.EnableGetRequests = developerFeatures;
            });

        return services;
    }

    /// <summary>
    /// Maps the endpoint. It is anonymous at the HTTP level so the IDE can load; every field enforces its own policy,
    /// so an unauthenticated query gets a GraphQL authorization error and no data.
    /// </summary>
    public static void MapOrdersGraphQL(this WebApplication app) =>
        app.MapGraphQL(Path).AllowAnonymous();
}
