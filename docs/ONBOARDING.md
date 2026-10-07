# Onboarding

Welcome. This guide gets you from a fresh clone to shipping a change: set up, find your way around, follow one
request end to end, then the recipes for the changes you'll make most often. The [README](../README.md) is the
reference; this is the walkthrough.

## Day one

### 1. Install

| Tool | Version | Why |
|---|---|---|
| .NET SDK | **10.0.302** (pinned in [global.json](../global.json)) plus the **.NET 8 runtime** | The services target net8.0; the Aspire AppHost needs the .NET 10 SDK |
| Node.js | 24 | The web app |
| Docker Desktop | current | RabbitMQ under Aspire, Testcontainers, Compose |
| SQL Server LocalDB | ships with Visual Studio, or the SQL Server Express installer | The local database under Aspire (Windows) |
| An editor | Rider, Visual Studio or VS Code | The `.http` files in [requests/](../requests/) use VS Code's REST Client extension (its request-chaining syntax); Postman users have [requests/postman/](../requests/postman/) |

Not on Windows? Skip LocalDB and use [Docker Compose](../README.md#1-docker-compose-everything-in-containers); the
integration tests use a SQL Server container automatically.

### 2. Run it

```bash
dotnet tool restore
```

```bash
dotnet run --project src/OrderManagement.AppHost
```

Open the Aspire dashboard link the console prints. Every resource (API, worker, web, RabbitMQ) is there with its
logs, traces and metrics. The app is at http://localhost:5173. You're signed in automatically as a development user
with all three roles.

### 3. Prove your setup

```bash
dotnet test
```

```bash
cd web && npm install && npm test
```

Both should be green before you change anything. If they aren't, see [Troubleshooting](#troubleshooting).

### 4. Click through the journey

Create a customer in Namibia, create an order for them, and watch the currency picker offer NAD or ZAR. Mark the
order paid: within a second or two the worker allocates stock and fulfils it, and the page updates by itself. In
the Aspire dashboard, open **Traces** and find that request: one trace goes API → SQL → RabbitMQ → worker → SQL.

## How the code is organised

```
Domain           Pure business rules. No EF, no ASP.NET, no I/O. Most unit tests live here.
Contracts        Messages between API and worker. Changing one is a contract change.
Infrastructure   Everything that talks to SQL Server or RabbitMQ.
Api              HTTP: controllers, request/response contracts, validators, services, auth, errors.
Worker           Message consumers.
ServiceDefaults  Shared hosting: logging, telemetry, health checks.
AppHost          Local orchestration only. Never deployed.
```

Dependencies point inwards: Api and Worker → Infrastructure → Domain. Domain depends on nothing.

There's deliberately **no MediatR, no generic repository, no unit of work.** A controller calls a service behind an
interface; the service uses `AppDbContext` directly. If you're tempted to add a layer, raise it first.

## Follow one request: creating an order

Read these in order; it's the fastest way to learn the codebase.

1. **[OrdersController.cs](../src/OrderManagement.Api/Controllers/V1/OrdersController.cs)**, `Create`. Thin: call
   the service, turn the result into 201 or a ProblemDetails. Policies (`Orders.Write`) and the write rate limit are
   attributes here.
2. **[OrderContracts.cs](../src/OrderManagement.Api/Contracts/Orders/OrderContracts.cs)** and
   **[OrderValidators.cs](../src/OrderManagement.Api/Contracts/Orders/OrderValidators.cs)**. The request shape and
   FluentValidation rules. Validators check *shape* and give friendly messages; they run automatically before the
   action.
3. **[OrderService.cs](../src/OrderManagement.Api/Services/OrderService.cs)**, `CreateAsync`. Loads the customer,
   asks the domain to build the order, saves it and publishes `OrderCreated`, all in one `SaveChangesAsync`.
4. **[Order.cs](../src/OrderManagement.Domain/Orders/Order.cs)**, `Create`. The business rules: currency allowed for
   the customer's country ([SadcCatalogue.cs](../src/OrderManagement.Domain/Sadc/SadcCatalogue.cs)), price precision
   per currency, duplicate SKUs, totals. It returns `ErrorOr<Order>`: every problem at once, no exceptions.
5. **The outbox.** The `OrderCreated` message is written to the `OutboxMessage` table in the same transaction as the
   order. MassTransit's delivery service publishes it to RabbitMQ afterwards. If RabbitMQ is down, orders are still
   saved, and the messages go out when it's back.
6. **[OrderCreatedConsumer.cs](../src/OrderManagement.Worker/Consumers/OrderCreatedConsumer.cs)**. Allocates stock
   and sets `AllocatedAt`. If the order was already paid, it fulfils it. The inbox makes redelivery harmless.
7. **[ErrorOrProblems.cs](../src/OrderManagement.Api/Common/Errors/ErrorOrProblems.cs)**. How a domain `Error`
   becomes a ProblemDetails with the right status, `code`, field errors and friendly text.

Status changes (`PUT /orders/{id}/status`) add two things worth reading next in the same service:
**idempotency** (the `Idempotency-Key` header and the `IdempotencyKeys` table: the same key replays the first
response, a different body is a 422) and **optimistic concurrency** (the rowversion is the ETag; a stale `If-Match`
is a 412, a lost race is a 409).

## The rules we hold to

- **Money is `decimal`, never `double`.** Totals are computed on the server only. A price can't have more decimals
  than its currency allows, so no rounding is ever needed.
- **Never mix currencies.** No conversion, and no sum or report across currencies.
- **Time is UTC.** Inject `TimeProvider`; never call `DateTime.Now` or `DateTime.UtcNow`. Tests use
  `FakeTimeProvider`.
- **Expected failures are `ErrorOr` values; exceptions are for bugs.** "Customer not found" and "invalid transition"
  are errors you return, not exceptions you throw.
- **Every error a user can see is plain English.** Write messages for the person reading them: say what's wrong and
  how to fix it ("Customers in South Africa can only order in ZAR, not USD."), never "Validation failed for field
  CurrencyCode".
- **Warnings are errors** ([Directory.Build.props](../Directory.Build.props)) and code style is enforced in the
  build ([.editorconfig](../.editorconfig)). Fix the warning; don't suppress it without a comment saying why.
- **Tests ship with the change**, not afterwards.

## Recipes

### Add an API endpoint

1. Request/response records and a validator in `src/OrderManagement.Api/Contracts/<Area>/`. Keep each response
   field non-nullable unless it really can be null: the OpenAPI document marks non-nullable response fields
   `required`, and the web app's types follow.
2. The business rule in the Domain, returning `ErrorOr`, with unit tests.
3. A service method (interface in `Services/Interfaces`), then a thin controller action with `[Authorize(Policy =
   ...)]` and `[ProducesResponseType]` for every status it can return.
4. Integration tests in `tests/OrderManagement.IntegrationTests/Api/` through the real HTTP pipeline. Use
   `fixture.Factory.CreateClientWithRoles(...)` to test 401/403 as well as the happy path.
5. Update the web app's types: with the API running, `cd web && npm run api:pull`. CI fails if
   `web/src/api/schema.d.ts` doesn't match `web/openapi/v1.json`.
6. Add the request to [requests/](../requests/) so the next person can try it.

### Change the database

```bash
EF="dotnet ef --project src/OrderManagement.Infrastructure --startup-project src/OrderManagement.Infrastructure"
```

```bash
$EF migrations add <Name> -o Persistence/Migrations
```

Then regenerate the SQL scripts (commands in [db/README.md](../db/README.md#commands)) and commit them with the
migration; CI fails if they're stale. Make the change **additive**: new nullable columns, new tables, new indexes. A
rename or drop is a multi-release change (expand → migrate → contract, described in db/README.md). Check that it
rolls back: `$EF database update <PreviousMigration>`.

### Publish or consume a message

1. The message record goes in [src/OrderManagement.Contracts/Orders/OrderEvents.cs](../src/OrderManagement.Contracts/Orders/OrderEvents.cs).
   Messages are a contract between deployable services: add fields, don't rename or remove them.
2. Publish through `IOrderEventPublisher` inside the same `SaveChangesAsync` as the state change, so the outbox
   keeps them atomic.
3. The consumer goes in `src/OrderManagement.Worker/Consumers/`. It must be **idempotent**: the inbox removes
   duplicate deliveries, but a retry can still run it twice, so re-check state before acting (see how
   `OrderCreatedConsumer` returns early for an allocated or cancelled order).
4. Test it in `tests/OrderManagement.IntegrationTests/Messaging/` with `WorkerHarness`, which runs the real consumers
   against the test database ([WorkerConsumerTests.cs](../tests/OrderManagement.IntegrationTests/Messaging/WorkerConsumerTests.cs)
   shows the pattern).

### Add a page to the web app

The web app has its own guide: [web/README.md](../web/README.md). In short:

- Data hooks go in `web/src/api/queries.ts`; pages go in `web/src/features/<area>/`, lazy-loaded from
  `web/src/app/routes.tsx`.
- Use the shadcn/ui components in `web/src/components/ui`.
- Put filters and paging in the URL.
- Every control needs a label. The Playwright accessibility scan fails the build on serious WCAG problems.

## Testing

| Layer | Where | Use it for |
|---|---|---|
| Unit | `tests/OrderManagement.UnitTests` | Domain rules, edge cases. Fast; no I/O. |
| Integration | `tests/OrderManagement.IntegrationTests` | Anything crossing HTTP, SQL or messaging: the real pipeline on real SQL Server. |
| Web unit | `web/src/**/*.test.tsx` | Pages and the data layer, API mocked with MSW. Query by role and label, as a user would. |
| End-to-end | `web/e2e` | The journeys that must never break, and accessibility. Keep this suite small. |

`ORDERS_TEST_SQL` picks the integration database: `container` (Testcontainers), `localdb`, or unset (container if
Docker is up, else LocalDB). Run one test with `dotnet test --filter "FullyQualifiedName~OrderLifecycleTests"`.

## Workflow

- **Branches:** small, focused, one capability each (`feature/...`, `chore/...`, `test/...`, `docs/...`). Each one
  builds green and leaves the app runnable.
- **Pull requests** run CI ([.github/workflows/ci.yml](../.github/workflows/ci.yml)): build and tests, migration
  scripts against a real SQL Server, the web checks, and the Playwright suite against the Compose stack. All of it
  must be green to merge. Pushes to `master` publish the container images.
- **Commit messages** say what changed and why. One logical change per commit.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Docker Desktop fails with `input/output error`, or every command returns 500 | The disk is full. Docker's virtual disk grows by several GB per image build. Free 10 GB or more, restart Docker Desktop, and if it persists use Troubleshoot → Clean / Purge data. |
| AppHost: RabbitMQ never starts | Docker isn't running. Start it, or run the AppHost with `--Messaging:Transport InMemory` (no worker processing). |
| `/readiness` is Unhealthy | It lists which check failed in Development. Usually SQL (LocalDB not started: `sqllocaldb start MSSQLLocalDB`) or the broker. |
| The web app can't reach the API (CORS error in the browser console) | The API only allows origins in `Cors:AllowedOrigins`. Under Aspire the app uses the API's **https** endpoint: the API redirects http → https, and a CORS preflight can't follow a redirect. |
| `dotnet ef` says the .NET 8 runtime is missing | Install the .NET 8 runtime next to the SDK 10. The design-time tools run the net8.0 project. |
| Integration tests are slow to start | The first run pulls the SQL Server image. Later runs reuse it. |
| A web test times out waiting for a page | Pages are lazy-loaded. Use `findBy...` (which waits), not `getBy...`, for anything on a page you just navigated to. |
| Playwright can't find a browser | Run `npx playwright install chromium` in `web/`. |
