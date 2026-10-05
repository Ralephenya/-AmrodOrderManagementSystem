using OrderManagement.Api;
using OrderManagement.Infrastructure;
using OrderManagement.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddInfrastructure();
builder.AddApi();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await app.Services.MigrateDatabaseAsync();
}

app.MapDefaultEndpoints();
app.UseApi();

await app.RunAsync();

/// <summary>Exposed so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
