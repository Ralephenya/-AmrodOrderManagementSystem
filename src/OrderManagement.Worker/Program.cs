var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// The OrderCreated consumer (MassTransit) is registered in feature/worker-allocation.

var host = builder.Build();
host.Run();
