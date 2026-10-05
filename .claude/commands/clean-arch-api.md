---
description: Scaffold a layered .NET 10 Web API (controllers/v1, ApiResponse envelope, ErrorOr, JWT, EF Core SQLite, Scalar, Serilog)
argument-hint: <domain description, e.g. "product catalog API">
allowed-tools: Bash, Read, Write, Edit, Glob, Grep
---

# Scaffold a layered .NET 10 Web API

Domain: **$ARGUMENTS**

If `$ARGUMENTS` is empty, ask the user what the API is for before doing anything else.

This follows a **specific house style** taken from a working reference solution. Follow it
exactly — do not substitute your own preferences, and do not "improve" it into Clean
Architecture with separate projects.

Prefer the `dotnet` CLI over hand-writing `.csproj` XML. Let `dotnet add package` resolve
the latest stable version — do not hardcode versions.

## Shape: ONE API project, layered by folder

Not four projects. Not a Dependency Rule. One web project plus one test project, at the
solution root (no `src/` prefix):

```
<Name>.API/
  Common/ApiResponse.cs                  the response envelope
  Controllers/v1/                        lowercase v1
  DTOs/Requests/  DTOs/Responses/        records, DataAnnotations on request records
  Data/AppDbContext.cs
  Data/Configurations/                   IEntityTypeConfiguration<T>, one per entity
  Data/Interceptors/                     AuditAndSoftDeleteInterceptor.cs
  Extensions/                            Application / Infrastructure / Presentation / Logging
  Interfaces/                            IAuditable.cs, ISoftDeletable.cs
  Mappings/                              static ToResponse() extension methods
  Middleware/GlobalExceptionMiddleware.cs
  Migrations/                            real EF migrations, not EnsureCreated
  Models/                                entities
  Services/  Services/Interfaces/
<Name>.API.Tests/
```

## Hard constraints

- **No MediatR. No generic repository. No unit-of-work.** Plain service classes; interface
  in `Services/Interfaces/`, implementation in `Services/`, injected via primary
  constructor (`public class CategoryService(AppDbContext db) : ICategoryService`).
  This is a deliberate readability choice — say so in your summary.
- **`ErrorOr` for expected failures.** Services return `Task<ErrorOr<T>>` /
  `Task<ErrorOr<Deleted>>`. Use `Error.NotFound(description: ...)` and
  `Error.Conflict(description: ...)`. Never throw for control flow.
- **Every response is wrapped in `ApiResponse<T>`** — never return a bare DTO.
- **Controllers return `IActionResult`**, not `ActionResult<T>`.
- **`int` primary keys**, named after the entity (`CategoryID`, `OrderID`), not `Guid Id`.
- Entities implement `ISoftDeletable` / `IAuditable` where it makes sense.

## The envelope

```csharp
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };
    public static ApiResponse<T> Fail(IEnumerable<string> errors) =>
        new() { Success = false, Errors = errors.ToList() };
    public static ApiResponse<T> Fail(string error) => Fail([error]);
}
```

## Controller pattern — copy this shape exactly

```csharp
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class CategoriesController(ICategoryService categoryService) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var result = await categoryService.GetByIdAsync(id, ct);
        return result.Match<IActionResult>(
            data => Ok(ApiResponse<CategoryResponse>.Ok(data)),
            errors => NotFound(ApiResponse<CategoryResponse>.Fail(errors.First().Description)));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await categoryService.DeleteAsync(id, ct);
        return result.Match<IActionResult>(
            _ => Ok(ApiResponse<object>.Ok(null!, "Category deleted.")),
            errors => errors.First().Type == ErrorOr.ErrorType.Conflict
                ? Conflict(ApiResponse<object>.Fail(errors.First().Description))
                : NotFound(ApiResponse<object>.Fail(errors.First().Description)));
    }
}
```

`[ApiController]` goes on **each controller** — there is no shared base class beyond
`ControllerBase`. Branch on `errors.First().Type` where a method has more than one
failure mode.

## Program.cs — thin, composed from Extensions/

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddLogging();
builder.Services
    .AddInfrastructure(builder.Configuration)
    .AddApplication()
    .AddPresentation(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandling();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Environment.IsDevelopment())
    app.MapApiDocumentation();

app.Run();

public partial class Program { }   // so the test project can use WebApplicationFactory
```

- `LoggingExtensions.AddLogging` — `builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration))`.
  **All Serilog config lives in `appsettings.json`** (`MinimumLevel`, `WriteTo` console +
  rolling file, `Enrich`) — no sink configuration in C#.
- `InfrastructureExtensions.AddInfrastructure` — registers the interceptor as a singleton,
  then `AddDbContext<AppDbContext>((sp, o) => o.UseSqlite(config.GetConnectionString("DefaultConnection")).AddInterceptors(sp.GetRequiredService<AuditAndSoftDeleteInterceptor>()))`.
- `ApplicationExtensions.AddApplication` — Scrutor by **namespace**, not name suffix:
  `services.Scan(s => s.FromAssemblyOf<ICategoryService>().AddClasses(c => c.InNamespaces("<Name>.API.Services")).AsImplementedInterfaces().WithScopedLifetime())`.
- `PresentationExtensions.AddPresentation` — `AddControllers()`, `AddOpenApi()`,
  `AddApiVersioning(...)`, JWT bearer auth, authorization. Plus a
  `MapApiDocumentation()` extension that maps OpenAPI + `MapScalarApiReference` (Scalar,
  **not** Swagger UI) and marks both `.AllowAnonymous()`.
- `AddPresentation` **must also override `ApiBehaviorOptions.InvalidModelStateResponseFactory`**
  — see "Known bug" below. This is not optional; without it the envelope constraint is
  violated for every DataAnnotations validation failure.

## Auth: JWT bearer

- `AuthController` (`[ApiVersion("1.0")]`, route `api/v{version:apiVersion}/[controller]`)
  with `POST login` and `POST register`.
- `AuthService` hashes with **BCrypt.Net-Next**, stores a `User` entity, and issues a
  signed JWT on successful login. Login returns
  `ApiResponse<AuthUserResponse>` where the response carries the token.
- Auth endpoints are `[AllowAnonymous]`; at least one other action carries `[Authorize]`
  so the gate is demonstrable.
- JWT signing key / issuer / audience come from `appsettings.json`, bound to an options
  class. `System.IdentityModel.Tokens.Jwt` is needed wherever the token is built.

## Data

- Entities in `Models/`, implementing `ISoftDeletable` (`bool IsDeleted`) and `IAuditable`
  (`DateTime CreatedAt`, `DateTime? ModifiedAt`).
- One `IEntityTypeConfiguration<T>` per entity in `Data/Configurations/`; `OnModelCreating`
  calls `modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly())`
  rather than configuring inline.
- Seed representative data with `HasData` and **static** dates (a `new DateTime(...)`
  constant, never `DateTime.UtcNow` — it breaks migration idempotency).
- **Real migrations**: `dotnet ef migrations add InitialCreate`. Do not use
  `EnsureCreated()` in the app.
- `Middleware/GlobalExceptionMiddleware` logs and returns
  `ApiResponse<object>.Fail("An unexpected error occurred. Please try again later.")` with
  a 500, exposed via a `UseExceptionHandling()` extension.
- **Never use `FindAsync(id)` for a by-id lookup on an `ISoftDeletable` entity** — it
  bypasses global query filters (documented EF Core behavior) and will happily return,
  update, or re-delete a soft-deleted row. Use `FirstOrDefaultAsync(x => x.<Entity>ID == id, ct)`
  in every service method, from the start — see `/add-ef-soft-delete` for the full
  writeup and the mutation test that proves it.

## Steps

1. `dotnet new sln`, `dotnet new webapi -n <Name>.API`, `dotnet new xunit -n <Name>.API.Tests`,
   add both to the solution. Delete the template's `WeatherForecast` sample.
2. Packages on `<Name>.API`: `ErrorOr`, `BCrypt.Net-Next`, `Asp.Versioning.Http`,
   `Microsoft.AspNetCore.OpenApi`, `Microsoft.EntityFrameworkCore.Sqlite`,
   `Microsoft.EntityFrameworkCore.Design`, `Microsoft.AspNetCore.Authentication.JwtBearer`,
   `System.IdentityModel.Tokens.Jwt`, `Scalar.AspNetCore`, `Scrutor`, `Serilog.AspNetCore`,
   `Serilog.Sinks.Console`, `Serilog.Sinks.File`.
3. Build the folder tree above, then one full vertical slice for the sample entity:
   Model → Configuration → DTOs → Mapping → Service+Interface → Controller.
4. `dotnet ef migrations add InitialCreate`.
5. Run `dotnet build` from the solution folder and fix everything before finishing.

## Known bug in the house style — fix it, don't just note it

**`[ApiController]`'s automatic model-state validation runs *before* any action code and
short-circuits with a raw ASP.NET `ProblemDetails` 400 — bypassing `ApiResponse<T>`
entirely.** This directly violates the "every response is wrapped in `ApiResponse<T>`"
hard constraint above, for every `[Required]`/`[MaxLength]` failure on a request DTO. The
reference solution this house style is copied from has this exact gap — it has DataAnnotations
on every request record and no test anywhere that exercises a validation failure, so the
bug is silent there too. Do not carry it forward silently.

**Required fix**, inside `PresentationExtensions.AddPresentation`, immediately after
`AddControllers()`:

```csharp
services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .SelectMany(e => e.Value!.Errors.Select(x => x.ErrorMessage))
            .ToList();

        return new BadRequestObjectResult(ApiResponse<object>.Fail(errors));
    };
});
```

**Companion trap**: under `<Nullable>enable</Nullable>`, `[ApiController]` treats *any*
non-nullable reference-type request property as implicitly required, even with no
`[Required]` attribute on it. A request record field like
`[MaxLength(2000)] string Description` will be rejected as missing on `null` even though
you never asked for that. Declare optional string fields as `string?` in the request
record, and `?? string.Empty` them when assigning into the entity in the service.

## Known gotchas (verified — do not rediscover these the hard way)

- **`dotnet new sln` on .NET 10 emits `.slnx`, not `.sln`.** Never pass an assumed
  `X.sln` to `dotnet build`; run bare `dotnet build` from the solution folder.
- On Asp.Versioning **10.x**, `Asp.Versioning.Http` alone is enough for controller
  `[ApiVersion]` and the `v{version:apiVersion}` route constraint — verified against a
  working repo with no `.Mvc` package present, direct or transitive. Add
  `Asp.Versioning.Mvc` + `.ApiExplorer` only if you want `SubstituteApiVersionInUrl`.
- **EF Core cannot translate a static/extension mapper inside `.Select()`.** Materialize
  first: `var rows = await db.X.ToListAsync(ct); return rows.Select(r => r.ToResponse()).ToList();`
- **`dotnet run` reads `Properties/launchSettings.json`, whose `applicationUrl` overrides
  `ASPNETCORE_URLS`.** Pass `--no-launch-profile --urls http://localhost:<port>` when
  verifying, or your curls hit a dead port.
- Transitive `NU1903` NuGet advisories come from the SDK's own graph — note them, don't
  chase them.

## Self-evaluation — mandatory, run before finishing

1. `dotnet build` exits 0 with zero errors and zero scaffold-attributable warnings.
2. Confirm the folder tree matches the layout above exactly — including **lowercase
   `Controllers/v1/`** and `Services/Interfaces/`.
3. Start the app (`--no-launch-profile --urls ...`) and verify against the live server:
   - `POST /api/v1/auth/login` returns a token inside an `ApiResponse` envelope
   - the `[Authorize]` action returns **401** without it and succeeds with it
   - a `GET` returns `{"success":true,"data":...}` — confirm the envelope is really there
   - a missing id returns **404** with `{"success":false,...,"errors":[...]}`
   - a `[Required]` validation failure (blank title, etc.) returns **400 wrapped in
     `ApiResponse<object>`** — `{"success":false,"errors":[...]}`, **not** a raw
     ASP.NET `ProblemDetails` body. If you see `"title":"One or more validation errors
     occurred."` instead, `InvalidModelStateResponseFactory` is missing or wrong — fix it
     before finishing.
4. Confirm the version segment is live in the route (hit `/api/v1/...`, don't just read
   the attribute).
5. Confirm Scalar loads at `/scalar/v1` and lists the endpoints.

## Output

- The project structure as a tree.
- Package versions actually installed (read them back from the `.csproj`).
- Two ready-to-run commands: log in and capture a token, then call the protected endpoint.
- One line noting the deliberate no-MediatR / no-generic-repository choice.
