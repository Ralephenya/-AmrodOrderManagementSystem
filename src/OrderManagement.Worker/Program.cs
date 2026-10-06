using OrderManagement.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddWorker(builder.Configuration);

var host = builder.Build();
await host.RunAsync();
