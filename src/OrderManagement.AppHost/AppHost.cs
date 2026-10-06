var builder = DistributedApplication.CreateBuilder(args);

// SQL Server LocalDB for local development (see appsettings.Development.json). Docker Compose and CI use a
// SQL Server container instead. The React web app is added in feature/web-app.
var ordersDb = builder.AddConnectionString("OrdersDb");

var api = builder.AddProject<Projects.OrderManagement_Api>("api")
    .WithReference(ordersDb)
    .WithHttpHealthCheck("/readiness"); // database and broker reachable

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

builder.Build().Run();
