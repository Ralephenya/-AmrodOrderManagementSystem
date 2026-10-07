# Amrod Order Management (SADC)

A full-stack order management system for customers in the 16 countries of the Southern African Development
Community: customers, orders priced in the currencies each country may use, a status workflow, and a worker that
allocates stock and fulfils paid orders. Built for the Amrod Intermediate Full-Stack Developer assessment.

- **New to the codebase?** Start with [docs/ONBOARDING.md](docs/ONBOARDING.md).
- **Written answers:** [ANSWERS.md](ANSWERS.md).
- **Database lifecycle** (migrations, scripts, zero-downtime, rollback): [db/README.md](db/README.md).
- **Web app details:** [web/README.md](web/README.md).

## Contents

- [What it does](#what-it-does)
- [Run it](#run-it)
- [Test it](#test-it)
- [API](#api)
- [Architecture](#architecture)
- [SADC currencies and the Common Monetary Area](#sadc-currencies-and-the-common-monetary-area)
- [Database lifecycle](#database-lifecycle)
- [Security](#security)
- [Observability](#observability)
- [Decisions and trade-offs](#decisions-and-trade-offs)
- [Known limitations](#known-limitations)

## What it does

| | |
|---|---|
| **Customers** | Create, search by name or email prefix, paged and sorted. Each customer belongs to a SADC country. |
| **Orders** | Create with line items in a currency the customer's country accepts. The server computes every total. |
| **Status workflow** | `Pending → Paid → Fulfilled`, and `Pending/Paid → Cancelled`. Status changes are idempotent (`Idempotency-Key`) and concurrency-safe (`If-Match` / rowversion). |
| **Worker** | Consumes `OrderCreated` and `OrderPaid` over RabbitMQ: allocates stock, then fulfils a paid, allocated order. |
| **Reports** | Top spenders per currency and running totals per customer, as hand-written SQL through Dapper. |
| **GraphQL** | A read-only `/graphql` endpoint: a customer's orders with nested line items, filtering, sorting and cursor paging. |
| **Web app** | React + TypeScript: dashboard, customers, orders, order details, light and dark themes. |

## Run it

Pick one. All three end with the app in a browser.

### 1. Docker Compose (everything in containers)

Needs Docker Desktop only. Builds and starts SQL Server, RabbitMQ, a one-shot migrator, the API, the worker and
the web app:

```bash
docker compose -f deploy/docker-compose.yml up --build
```

| | |
|---|---|
| App | http://localhost:8080 |
| API docs | http://localhost:5000/swagger (Swagger UI) and http://localhost:5000/scalar/v1 (Scalar) |
| RabbitMQ | http://localhost:15672 (guest / guest) |
| SQL Server | `localhost,14333`, user `sa` (password in [deploy/docker-compose.yml](deploy/docker-compose.yml); override it in `deploy/.env`) |

The first build takes a few minutes and needs roughly 10 GB of free disk for images. `docker compose -f
deploy/docker-compose.yml down --volumes` removes the stack and its data.

### 2. .NET Aspire (local development)

Needs the [.NET SDK 10.0.302](global.json) with the .NET 8 runtime, Node 24, SQL Server LocalDB (Windows) and
Docker Desktop (for RabbitMQ). One command runs the API, worker, web app and broker, plus the Aspire dashboard:

```bash
dotnet run --project src/OrderManagement.AppHost
```

| | |
|---|---|
| Aspire dashboard (logs, traces, metrics) | https://localhost:17209 (the console prints a login link) |
| App | http://localhost:5173 |
| API | https://localhost:7140 (`/swagger`, `/scalar/v1`) |

Before the first run, make sure Docker Desktop is running and `node -v` shows 24 or later. You don't need to run
`npm install` yourself: the `web-installer` step does it each time the AppHost starts. If `web` shows *Finished*
in the dashboard, its packages were probably installed with an older Node. Delete `web/node_modules` and start
the AppHost again.

The API applies migrations to LocalDB on start-up in Development. **No Docker?** Run the AppHost with
`--Messaging:Transport InMemory`: the API works and writes events to the outbox, but no worker receives them, so
orders aren't allocated or fulfilled.

### 3. Projects one by one

```bash
dotnet run --project src/OrderManagement.Api --launch-profile http
```

```bash
dotnet run --project src/OrderManagement.Worker
```

```bash
cd web && npm install && npm run dev
```

The API listens on http://localhost:5050 and the app on http://localhost:5173. Both API and worker expect
RabbitMQ on `localhost:5672`.

### Signing in

There is no login screen in development. The API runs in **mock Entra** mode and the web app gets a token from
`POST /api/v1/dev/token` with the `Orders.Read`, `Orders.Write` and `Orders.Admin` roles. That endpoint only exists
in Development and Testing. Production uses real Microsoft Entra ID tokens (`Auth:Mode = Entra`, configured under
`AzureAd`), and the API refuses to start in Production with mock auth.

## Test it

| Suite | Command | What it covers |
|---|---|---|
| .NET unit (133) | `dotnet test tests/OrderManagement.UnitTests` | Domain: money rules, SADC catalogue, the order state machine, allocation |
| .NET integration (159) | `dotnet test tests/OrderManagement.IntegrationTests` | The real API over HTTP (WebApplicationFactory) on real SQL Server: endpoints, validation, auth, ETags, idempotency, concurrency, outbox, consumers, reports, GraphQL, OpenAPI shape |
| Web unit (47) | `cd web && npm test` | Pages and data layer with Vitest + React Testing Library, the API mocked with MSW |
| End-to-end (17) | `cd web && npm run e2e` | Playwright in Chromium against the real API, worker, SQL Server and RabbitMQ: the full order journey plus an axe WCAG 2.1 AA scan of every page in both themes |

Integration tests pick SQL Server automatically: a Testcontainers container when Docker is running, LocalDB
otherwise. Force one with `ORDERS_TEST_SQL=container` or `ORDERS_TEST_SQL=localdb`.

The e2e suite starts its own isolated stack (separate database and ports). To run it against a stack that's already
up, set `E2E_BASE_URL`, for example `E2E_BASE_URL=http://localhost:8080 npx playwright test` against Compose.

**CI** ([.github/workflows/ci.yml](.github/workflows/ci.yml)) runs on every pull request:

- **api:** build (warnings are errors), unit tests, integration tests on a SQL Server container.
- **migrations:** checks `db/scripts` matches the migrations, then applies, re-applies, rolls back and rolls forward
  on an empty SQL Server.
- **web:** lint, the generated API types match the OpenAPI document, typecheck, tests, build.
- **e2e:** brings the Compose stack up and runs the Playwright suite against it.
- **images:** on pushes to `master`, publishes the four images to GitHub Container Registry.

## API

Every route is versioned (`/api/v1/...`). The brief's unversioned routes (`/api/orders`) are aliases of v1.
Interactive docs are at `/swagger` and `/scalar/v1` in Development. Ready-made requests are in
[requests/](requests/) (`.http` files for VS Code's REST Client extension; they chain requests with its syntax) and
[requests/postman/](requests/postman/) (a Postman collection).

Roles are hierarchical: `Orders.Admin` can do everything `Orders.Write` can, and `Orders.Write` everything
`Orders.Read` can.

| Method | Route | Policy | Notes |
|---|---|---|---|
| `POST` | `/api/v1/customers` | Orders.Write | 201 + `Location`. Email unique (case-insensitive), 409 if taken |
| `GET` | `/api/v1/customers/{id}` | Orders.Read | |
| `GET` | `/api/v1/customers?search=&page=&pageSize=&sort=` | Orders.Read | Name or email **starts with** `search`; sort `name` / `createdAt`, `-` for descending |
| `POST` | `/api/v1/orders` | Orders.Write | 201 + `Location` + `ETag`. The server computes totals; it starts `Pending` |
| `GET` | `/api/v1/orders/{id}` | Orders.Read | With line items. `ETag`; `If-None-Match` gives 304 |
| `GET` | `/api/v1/orders?customerId=&status=&page=&pageSize=&sort=` | Orders.Read | Sort `createdAt` / `total`, default newest first |
| `PUT` | `/api/v1/orders/{id}/status` | Orders.Write | `Idempotency-Key` required; optional `If-Match`. Replays return the first response with `Idempotent-Replayed: true` |
| `GET` | `/api/v1/reference/countries` | Orders.Read | The 16 SADC countries and the currencies each may use |
| `GET` | `/api/v1/reports/top-spenders?currency=&days=&top=` | Orders.Admin | Paid + Fulfilled spend in one currency, customers with no spend included at 0 |
| `GET` | `/api/v1/reports/customers/{id}/running-totals?currency=` | Orders.Admin | Cumulative spend per order (window function) |
| `POST` | `/api/v1/dev/token` | anonymous | Development and Testing only |
| `GET` | `/healthz`, `/readiness` | anonymous | Liveness (process up); readiness (SQL + RabbitMQ). Details only in Development |
| `POST` | `/graphql` | Orders.Read (per field) | Read-only GraphQL, below |

### GraphQL

A read-only GraphQL endpoint sits next to the REST API at `/graphql`: a customer's orders with nested line items,
with filtering, sorting and cursor paging. Writes stay on REST, with its validation, idempotency keys and concurrency
checks. In Development, open http://localhost:5050/graphql in a browser for the Nitro IDE. Example queries are in
[requests/graphql.http](requests/graphql.http).

```graphql
query ($customerId: UUID!) {
  ordersByCustomer(customerId: $customerId, first: 10,
                   where: { status: { eq: PAID } }, order: [{ createdAt: DESC }]) {
    totalCount
    pageInfo { hasNextPage endCursor }
    nodes { id status currencyCode totalAmount createdAt
            lineItems { productSku quantity unitPrice lineTotal } }
  }
}
```

- **One SQL query per request.** The resolver returns an EF Core `IQueryable`, and Hot Chocolate composes the
  filter, sort, page and the selected fields onto it, so orders and their line items load together with only the
  requested columns. No N+1 (a test counts the SQL commands).
- **Same protections as REST:**
  - Every field requires `Orders.Read`.
  - Pages are capped at 100.
  - Query depth is limited to 8.
  - Hot Chocolate's cost analysis rejects queries built to be expensive.
- **Developer features only in Development and Testing:** the schema (introspection), the IDE and detailed errors.

**Errors** from the REST API are RFC 7807 ProblemDetails, always with a plain-English `title` and `detail`, a stable
`code`, field errors under `errors`, and a `correlationId` to quote to support. (GraphQL errors follow the GraphQL
spec instead: an `errors` array whose `extensions` carry a `code` and the same `correlationId`.)

```json
{
  "title": "Some details need fixing",
  "status": 400,
  "detail": "Please correct the highlighted fields and try again.",
  "code": "validation_failed",
  "errors": { "currencyCode": ["Customers in South Africa can only order in ZAR, not USD."] },
  "correlationId": "9a7256c4fceb742ec3b8aa17dd315d30"
}
```

## Architecture

```
src/
  OrderManagement.Domain/          Entities, the order state machine, the SADC catalogue, money rules. No dependencies.
  OrderManagement.Contracts/       Messages (OrderCreated, OrderPaid) and shared constants.
  OrderManagement.Infrastructure/  EF Core (DbContext, configurations, migrations), Dapper reports, MassTransit with
                                   the EF outbox/inbox, the idempotency store.
  OrderManagement.Api/             Controllers, validators, auth, ProblemDetails, CORS, ETags, OpenAPI.
  OrderManagement.Worker/          MassTransit consumers: allocate stock, fulfil paid orders.
  OrderManagement.ServiceDefaults/ Serilog, OpenTelemetry, health checks, resilience.
  OrderManagement.AppHost/         .NET Aspire: runs everything locally.
web/                               React + TypeScript (Vite), Playwright tests in web/e2e.
tests/                             .NET unit and integration tests.
db/                                Generated migration SQL and the database lifecycle guide.
deploy/                            Dockerfiles, nginx config, docker-compose.yml.
```

```
 Browser ── React app (TanStack Query, client typed from OpenAPI)
    │  Bearer JWT · Idempotency-Key · If-Match / If-None-Match · X-Correlation-ID
    ▼
 API ── correlation id → security headers → CORS → auth → policy → controller → validator → service (ErrorOr)
    │                                                                                        │
    │ one transaction: Order + LineItems + OrderCreated outbox row                     reads: EF projections, Dapper
    ▼
 SQL Server ── MassTransit outbox ──► RabbitMQ ──► Worker (inbox de-duplication, retry, then *_error queue)
                                                     └─ allocates stock, fulfils the order once it is also paid
```

**Creating an order** validates the request, checks the customer and that the currency is allowed for their country,
computes totals, and saves the order, its lines and the `OrderCreated` message **in one transaction** (the
transactional outbox). It returns 201: the order exists and is readable straight away. The broker carries only the
follow-on work, so a RabbitMQ outage never loses or blocks an order.

**The worker** allocates stock when an order is created. When an order is paid and allocated, it moves it to
Fulfilled. The inbox drops duplicate deliveries; a failing message is retried 5 times with exponential back-off (1 s growing
to 30 s, `Messaging:Retry`) and then lands in a dead-letter `*_error` queue. The correlation id travels in message headers, so one trace spans API → broker → worker.

## SADC currencies and the Common Monetary Area

- All 16 SADC member states are supported, each with the ISO 4217 currencies its customers may order in
  (`GET /api/v1/reference/countries`).
- **Common Monetary Area:** customers in Namibia, Lesotho and Eswatini may order in their own currency (NAD, LSL,
  SZL) **or in ZAR**, which is legal tender there at par. South Africa orders in ZAR. Zimbabwe allows ZWL and USD.
  ZWG replaced ZWL in April 2024; the catalogue is one entry to update.
- **Money is exact.** Amounts are `decimal(18,2)`, and a unit price may not have more decimals than its currency
  allows (KMF has none). So `Σ quantity × unit price` needs no rounding, and the server computes every total.
- **Nothing is ever converted or summed across currencies.** Reports always take one currency.

## Database lifecycle

The schema is owned by EF Core code-first migrations (six so far). The full guide, with commands, is
[db/README.md](db/README.md). In short:

- **Local development:** the API applies pending migrations on start-up (`Database:ApplyMigrationsOnStartup`, on
  only in `appsettings.Development.json`).
- **Shared environments never migrate from the app.** Deploys apply the reviewed idempotent script
  ([db/scripts/migrate-idempotent.sql](db/scripts/migrate-idempotent.sql)) or a migration bundle as a separate step.
  Compose does exactly that with its `migrations` service, before the API starts.
- **Zero downtime:** expand → migrate → contract. Additive changes ship first, code moves to the new shape and
  backfills in batches, and constraints or drops come in a later release. No rename or drop ships in the same
  release as the code that stops using it.
- **Rollback:** every migration so far is additive, so it rolls back cleanly to the previous one. Down-scripts are
  generated per migration (`db/scripts/rollback-*.sql`). CI applies, re-applies, rolls back and re-applies the
  scripts against a real SQL Server on every pull request.

## Security

- **Authentication:** Microsoft Entra ID through Microsoft.Identity.Web; a mock issuer for development and tests only.
- **Authorization:** three policies from the token's `roles` claim (`Orders.Read`, `Orders.Write`, `Orders.Admin`),
  and a fallback policy that requires sign-in everywhere else.
- **CORS:** only origins listed in `Cors:AllowedOrigins`, per environment; no wildcard. Compose avoids CORS entirely
  by serving the app and API on one origin (nginx proxies `/api`).
- **Rate limiting:** per user (the token's `oid`, or the IP address when anonymous), in two layers: 600 requests a
  minute for everything (REST, GraphQL, reports, the dev token endpoint) and 120 a minute for writes. Over the limit
  the API returns a 429 problem with `Retry-After`. Health probes are exempt. Both limits are in `RateLimiting:*`.
- **Hardening:** security headers, HTTPS redirection and HSTS outside development, request size limits, no stack
  traces in responses, containers running as non-root users.

## Observability

- **Logs:** Serilog, structured. Readable in development and JSON elsewhere, with correlation id and user id on every
  request line.
- **Traces and metrics:** OpenTelemetry across API, broker and worker, plus custom counters (orders created by
  currency, status changes, allocations, fulfilments). All of it is visible in the Aspire dashboard locally.
- **Health:** `/healthz` (liveness) and `/readiness` (SQL Server and RabbitMQ). Compose and the AppHost both wait
  on `/readiness`.

## Decisions and trade-offs

| Decision | Why |
|---|---|
| Services on **.NET 8**, AppHost on .NET 10 | The brief asks for .NET 8; Aspire 13 needs a .NET 10 AppHost, which only orchestrates. |
| **201** for order creation, not 202 | The order is committed before the response. The broker carries downstream work only. |
| Worker fulfils only **paid** orders | The brief has the worker move an order to Fulfilled on `OrderCreated`. With the status flow Pending → Paid → Fulfilled, shipping an unpaid order would be wrong, so on `OrderCreated` the worker allocates stock, and it fulfils once the order is both paid and allocated (whichever event comes second triggers it). |
| Transactional **outbox** (MassTransit + EF) | No dual write: the order and its event commit together or not at all. |
| **MassTransit 8**, pinned | v9 needs a commercial licence. v8 has the outbox, inbox, retries and dead-letter queues we need. |
| **ErrorOr** for expected failures | Validation and business errors are values; exceptions are for bugs and infrastructure. |
| **ProblemDetails** with friendly text | Machine-readable for tools, readable for people. The web app shows it as is. |
| **EF Core for writes, Dapper for reports** | EF handles aggregates and migrations; reports are SQL a reviewer can read. |
| **Shouldly** over FluentAssertions | FluentAssertions 8 needs a paid licence. |
| Mock Entra in development | Every auth path runs for real (policies, roles, 401/403) without a tenant. |
| Same-origin nginx proxy in Compose | No CORS configuration to get wrong in the container setup. |

## Known limitations

- **Cancelling an order doesn't release its allocated stock.** Allocation is simulated (`IStockAllocator`); a real
  allocator would need a compensating release, consumed from an `OrderCancelled` event.
- **The dashboard makes four requests for its status counts** (one list query per status). A small summary
  endpoint would replace them.
- **Compose runs the API in Development,** so the mock sign-in works without a tenant. A real deployment would set
  `ASPNETCORE_ENVIRONMENT=Production` and configure Entra.
- **ZWL** is still the catalogue's Zimbabwe currency; ZWG replaced it in April 2024.
- **No server-side cache.** Orders are served fresh (with ETags and 304s), and reference data is cached in the
  browser. A Redis-backed `HybridCache` for rarely-changing data is sketched, commented out, at the end of
  `ApiSetup.cs`, ready to switch on when there is data that needs it.
- **No coverage summary yet.** CI publishes test results (`.trx`) and, on failure, the Playwright report. Adding
  coverlet with a ratchet (no drop below the current figure) is the next CI step.
