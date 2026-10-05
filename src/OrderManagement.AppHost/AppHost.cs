var builder = DistributedApplication.CreateBuilder(args);

// SQL (LocalDB), RabbitMQ and the React web app are added in their own feature branches.

var api = builder.AddProject<Projects.OrderManagement_Api>("api")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.OrderManagement_Worker>("worker")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
