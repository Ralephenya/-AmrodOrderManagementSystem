---
description: Generate a complete new vertical slice (Model → Config → DTOs → Mapping → Service → Controller) matching the existing codebase
argument-hint: <feature, e.g. "add a Category entity with CRUD">
allowed-tools: Bash, Read, Write, Edit, Glob, Grep
---

# Quick vertical slice

Feature: **$ARGUMENTS**

If `$ARGUMENTS` is empty, ask what to build. This is the fast path — aim for a complete,
building slice, not a discussion.

## Step 0 — read first, write second (mandatory)

Before writing a single line, read the existing sample entity's **full** slice. In this
house style that is seven files:

| Layer | Path |
|---|---|
| Entity | `Models/<Entity>.cs` |
| EF config | `Data/Configurations/<Entity>Configuration.cs` |
| Requests | `DTOs/Requests/Create<Entity>Request.cs`, `Update<Entity>Request.cs` |
| Response | `DTOs/Responses/<Entity>Response.cs` |
| Mapping | `Mappings/<Entity>Mappings.cs` |
| Service | `Services/Interfaces/I<Entity>Service.cs`, `Services/<Entity>Service.cs` |
| Controller | `Controllers/v1/<Entity>sController.cs` |

**Mirror that structure and naming exactly. Do not invent a new style, a new error-handling
approach, or a new folder.** Consistency beats your preferences here.

## Steps

1. **Model** (`Models/`) — `int` PK named after the entity (`CategoryID`, not `Id`),
   implementing `ISoftDeletable` / `IAuditable` if the existing entity does. Navigation
   collections initialised to `[]`.
2. **Configuration** (`Data/Configurations/`) — an `IEntityTypeConfiguration<T>` with
   `HasKey`, `IsRequired()`, `HasMaxLength(...)`, and relationships. It is picked up
   automatically by `ApplyConfigurationsFromAssembly` — no `OnModelCreating` edit needed.
   Add the `DbSet<T>` to `AppDbContext`.
3. **DTOs** (`DTOs/Requests/`, `DTOs/Responses/`) — `record`s. Request records carry
   `[Required]` / `[MaxLength(...)]` DataAnnotations inline on the parameters.
4. **Mapping** (`Mappings/`) — a static class with a `ToResponse()` extension method.
5. **Service** — interface in `Services/Interfaces/` returning `Task<ErrorOr<T>>` /
   `Task<ErrorOr<Deleted>>`; implementation in `Services/` with a primary constructor
   taking `AppDbContext db`. Use `Error.NotFound(description: ...)` and
   `Error.Conflict(description: ...)` for guard conditions. Scrutor registers it
   automatically via the `Services` namespace scan — do **not** hand-register it.
6. **Controller** (`Controllers/v1/`) — `[ApiController]`, `[ApiVersion("1.0")]`,
   `[Route("api/v{version:apiVersion}/[controller]")]`, primary-constructor injection,
   every action returning `IActionResult` and wrapping in `ApiResponse<T>`:
   ```csharp
   return result.Match<IActionResult>(
       data => Ok(ApiResponse<XResponse>.Ok(data)),
       errors => NotFound(ApiResponse<XResponse>.Fail(errors.First().Description)));
   ```
   Branch on `errors.First().Type == ErrorOr.ErrorType.Conflict` where a method has more
   than one failure mode.
7. **Migration** — the project uses real migrations, so
   `dotnet ef migrations add Add<Entity>` and confirm it generates the table. (If a
   `Migrations/` folder somehow doesn't exist, say so rather than silently switching to
   `EnsureCreated()`.)
8. Run `dotnet build` and fix errors before finishing. The solution file may be `.slnx` —
   run bare `dotnet build` from the solution folder.

## Gotchas

- **EF Core cannot translate `.ToResponse()` inside `.Select()`** — it compiles and throws
  at runtime. Materialize first:
  `var rows = await db.X.ToListAsync(ct); return rows.Select(r => r.ToResponse()).ToList();`
- `HasData` seeding must use **static** dates (`new DateTime(2026, 1, 1, ...)`), never
  `DateTime.UtcNow`, or every `dotnet ef migrations add` produces a spurious diff.
- **New request DTOs inherit the house style's `[ApiController]` validation bug if
  `AddPresentation`'s `InvalidModelStateResponseFactory` override isn't already in
  place** — check it's there before assuming a blank-field 400 will come back wrapped
  in `ApiResponse<object>` like every other error.
- Under `<Nullable>enable</Nullable>`, any non-nullable `string` property on a request
  record is implicitly required even with no `[Required]` attribute. Optional string
  fields must be declared `string?` and `?? string.Empty`'d when assigned to the entity.
- **If the new entity implements `ISoftDeletable`, use `FirstOrDefaultAsync(x => x.<Entity>ID == id, ct)`
  for every by-id lookup in the service — never `FindAsync`.** `FindAsync` bypasses global
  query filters and will read/update/re-delete a soft-deleted row.

## Self-evaluation — mandatory

1. `dotnet build` succeeds with zero errors.
2. Start the app and confirm the new endpoints appear in the OpenAPI / Scalar document —
   not just that the code compiles.
3. Hit create and get-by-id for real, and confirm the response is wrapped in the
   `ApiResponse` envelope (`{"success":true,"data":{...}}`), not a bare DTO.
4. Style audit: compare your seven new files against the existing entity's seven.
   Confirm naming, folder placement, `int` key naming, DataAnnotations placement, and
   error handling genuinely match. **If you deviated anywhere, call it out and say why** —
   silent divergence is the failure mode here.

## Output

The new endpoints as a table (method, route, purpose), plus example request and response
bodies for create and get-by-id — using **real values from the calls you made**, not
invented ones.
