using OrderManagement.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddInfrastructure();

// The OrderCreated consumer (MassTransit) is registered in feature/messaging.

var host = builder.Build();
await host.RunAsync();
