---
description: Add a real xUnit test project (TestBase + WebApplicationFactory integration tests) to the layered API
argument-hint: [optional: what to focus tests on]
allowed-tools: Bash, Read, Write, Edit, Glob, Grep
---

# Add tests to the API solution

Focus (optional): **$ARGUMENTS**

Assumes a solution shaped by `/clean-arch-api`: one `<Name>.API` web project layered by
folder, `ApiResponse<T>` envelope, `ErrorOr` services, controllers under
`Controllers/v1/`. Generate **real working tests, not placeholders** — no
`Assert.True(true)`, no `[Fact(Skip=...)]` filler.

## Step 0 — read before writing

Read the real code first so tests use real type names and real routes:
- `Models/` and `Data/AppDbContext.cs` (keys are `int`, named `CategoryID` etc.)
- `Services/Interfaces/` + the matching implementation
- `Common/ApiResponse.cs`
- a controller in `Controllers/v1/` — note the exact route and status codes
- `Program.cs` and `Extensions/PresentationExtensions.cs` (auth setup)

Do not invent method names. Mirror what is there.

## Structure

Mirror the API's folders inside the test project:

```
<Name>.API.Tests/
  TestBase.cs                       factory + shared HttpClient
  Controllers/v1/                   integration tests per controller
  Services/                         service-level tests
  Data/Interceptors/                interceptor tests
  appsettings.Testing.json
```

Add to the test `.csproj`:
```xml
<ItemGroup><Using Include="Xunit" /></ItemGroup>
<ItemGroup><Content Include="appsettings.Testing.json" CopyToOutputDirectory="Always" /></ItemGroup>
```

## TestBase — the house pattern

A `WebApplicationFactory<Program>` subclass that:
- pushes test config in via `ConfigureAppConfiguration` + `AddInMemoryCollection`
  (JWT key/issuer/audience, quiet Serilog: `["Serilog:MinimumLevel:Default"] = "Warning"`)
- removes the existing `DbContextOptions<AppDbContext>` descriptor and re-adds SQLite
  pointing at a **per-run unique file** (`$"test_{Guid.NewGuid():N}.db"`), re-attaching the
  interceptor from DI so soft-delete/audit behaviour is still exercised
- `builder.UseEnvironment("Testing")`

Plus an `abstract class TestBase : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime`
that exposes `Client` and `Factory`, calls `EnsureCreatedAsync()` in `InitializeAsync` and
`EnsureDeletedAsync()` in `DisposeAsync`. Tests then read
`public class CategoriesControllerTests(ApiWebApplicationFactory factory) : TestBase(factory)`.

## What to generate

- **Service tests (≥2)** — happy path and an `ErrorOr` failure path (missing id returns
  `ErrorType.NotFound`; a guarded delete returns `ErrorType.Conflict`). AAA with
  `// Arrange` / `// Act` / `// Assert` comments, asserted with FluentAssertions.
- **Controller integration tests (≥3)** through `Client`, deserializing into the envelope:
  `await response.Content.ReadFromJsonAsync<ApiResponse<CategoryResponse>>()`, then
  asserting `body!.Success` and `body.Data`. Cover 200, 201, and 404.
- **An auth test**: the `[Authorize]` action returns **401** with no token, and succeeds
  with one obtained from `POST /api/v1/auth/login`.
- **An interceptor test**: delete an entity, assert a normal query no longer returns it,
  and that `IgnoreQueryFilters()` still finds the row with `IsDeleted == true`.

Run `dotnet test`. **All generated tests must pass before you declare done.** Fix
failures rather than weakening assertions.

## Known gotchas (verified — do not rediscover these the hard way)

- **`FluentAssertions` 8.x is not free for commercial use** (Xceed licence; free only for
  personal/OSS). The reference solution pins **8.3.0**. If this is work code, either pin
  `--version 7.0.0` (last MIT release) or use `Shouldly`. **Ask the user before adding it.**
- **`WebApplicationFactory<T>` already defines `DisposeAsync`** — implementing
  `IAsyncLifetime` on the factory itself clashes; put `IAsyncLifetime` on `TestBase`
  instead (as above), or declare `public new Task DisposeAsync()`.
- Swapping the DbContext needs the **descriptor** removed
  (`services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>))`)
  before re-adding, or the original registration stays live and tests hit the dev database.
- If you use a shared in-memory SQLite connection instead of a file, it **must stay open**
  for the fixture's lifetime — closing it drops the database and every context then sees
  an empty schema, which produces false passes.
- Delete the xunit template's `UnitTest1.cs`.
- The solution file may be `.slnx` — run bare `dotnet test` from the solution folder.

## Self-evaluation — mandatory

1. `dotnet test` runs and every generated test passes.
2. Tautology check — **by real mutation, not by reading.** Break two load-bearing
   behaviours (e.g. a validation/conflict guard, and the `[Authorize]` attribute),
   re-run `dotnet test` each time, and confirm the *specific* tests you expect actually
   fail. Then revert and confirm green again. A test that passes with the logic removed
   is worthless — rewrite it.
   **Verify each revert by grepping for the mutated text. Leave no broken code behind.**
3. Confirm at least one test asserts on the `ApiResponse` envelope itself
   (`Success`/`Errors`), not only on the HTTP status code.

## Output

- What is tested, by layer.
- What was deliberately left out and why.
- Any deviation from the existing codebase's conventions, called out explicitly.
