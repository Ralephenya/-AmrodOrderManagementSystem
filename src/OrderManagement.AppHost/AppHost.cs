var builder = DistributedApplication.CreateBuilder(args);

// SQL Server LocalDB for local development (see appsettings.Development.json). Docker Compose and CI use a
// SQL Server container instead. RabbitMQ and the React web app are added in their own feature branches.
var ordersDb = builder.AddConnectionString("OrdersDb");

var api = builder.AddProject<Projects.OrderManagement_Api>("api")
    .WithReference(ordersDb)
    .WithHttpHealthCheck("/health");

// The API applies migrations on startup in Development, so the worker waits until the API is healthy.
builder.AddProject<Projects.OrderManagement_Worker>("worker")
    .WithReference(ordersDb)
    .WaitFor(api);

builder.Build().Run();
