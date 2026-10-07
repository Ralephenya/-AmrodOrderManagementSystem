var builder = DistributedApplication.CreateBuilder(args);

// SQL Server LocalDB for local development (see appsettings.Development.json). Docker Compose and CI use a
// SQL Server container instead.
var ordersDb = builder.AddConnectionString("OrdersDb");

var api = builder.AddProject<Projects.OrderManagement_Api>("api")
    .WithReference(ordersDb)
    .WithHttpHealthCheck("/readiness") // database and broker reachable
    .WithUrls(context =>
    {
        // Link the API docs from the dashboard instead of the bare endpoints (which have no page at "/").
        var https = context.Urls.FirstOrDefault(u => u.Endpoint?.EndpointName == "https");
        if (https is null)
        {
            return;
        }

        context.Urls.Clear();
        context.Urls.Add(new() { Url = $"{https.Url}/scalar", DisplayText = "Scalar" });
        context.Urls.Add(new() { Url = $"{https.Url}/swagger", DisplayText = "Swagger" });
    });

// The API applies migrations on startup in Development, so the worker waits until the API is healthy.
var worker = builder.AddProject<Projects.OrderManagement_Worker>("worker")
    .WithReference(ordersDb)
    .WaitFor(api);

// RabbitMQ runs as a container, which needs Docker. Without Docker, start the AppHost with
// `--Messaging:Transport InMemory`: the API still works and writes events to the outbox, but no worker receives them.
if (string.Equals(builder.Configuration["Messaging:Transport"], "InMemory", StringComparison.OrdinalIgnoreCase))
{
    api.WithEnvironment("Messaging__Transport", "InMemory");
    worker.WithEnvironment("Messaging__Transport", "InMemory");
}
else
{
    // Data volume: messages the outbox already handed to the broker survive an AppHost restart.
    var messaging = builder.AddRabbitMQ("messaging")
        .WithDataVolume("order-management-rabbitmq")
        .WithManagementPlugin(); // management UI linked from the dashboard
    api.WithReference(messaging).WaitFor(messaging);
    worker.WithReference(messaging).WaitFor(messaging);
}

// The React app (Vite dev server). It is served on 5173, the origin the API's Development CORS policy allows.
// It calls the API over HTTPS: the API redirects HTTP to HTTPS, and a browser's CORS preflight can't follow
// a redirect.
builder.AddViteApp("web", "../../web")
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WithEnvironment("VITE_API_BASE_URL", api.GetEndpoint("https"))
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
