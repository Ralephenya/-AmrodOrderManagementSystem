# Written answers

Answers to the assessment's general and SQL questions. Where the codebase demonstrates an answer, the answer links
to it, so each claim can be checked against working code. The SQL runs against this project's schema (see
[db/scripts/migrate-idempotent.sql](db/scripts/migrate-idempotent.sql)): `Customers`, `Orders`, `OrderLineItems`,
with `Orders.Status` stored as `varchar(16)`, `CurrencyCode` as `char(3)` and money as `decimal(18,2)`.

**General:** [1 async/await](#1-asyncawait) · [2 Minimal APIs vs controllers](#2-minimal-apis-vs-controllers) ·
[3 Tracking and concurrency](#3-tracking-vs-asnotracking-and-optimistic-concurrency) ·
[4 Validation](#4-where-business-rules-live) · [5 Error handling](#5-global-exception-handling-and-problemdetails) ·
[6 API design](#6-rest-search-endpoints-graphql-and-n1) · [7 Security](#7-entra-jwt-validation-and-policies) ·
[8 Messaging](#8-rabbitmq-exchanges-durability-delivery-and-idempotency) · [9 Frontend](#9-frontend-state-and-type-safe-contracts) ·
[10 Testing/CI](#10-test-pyramid-and-ci-gates) · [11 Performance](#11-performance-scalability-and-observability)

**SQL:** [12 Pagination](#12-pagination-query) · [13 Top spenders](#13-top-spenders-last-90-days) ·
[14 Indexing](#14-indexing) · [15 Key lookups](#15-execution-plans-and-key-lookups) ·
[16 Rowversion](#16-optimistic-concurrency-with-rowversion) · [17 Deadlocks](#17-deadlocks) ·
[18 Running totals](#18-window-functions-running-total) · [19 Partitioning](#19-partitioning-strategy)

---

## General questions

### 1. async/await

> Explain async/await best practices in ASP.NET Core for I/O-bound work. When (if ever) would you use `Task.Run`
> in a web API?

A request thread should never sit blocked waiting on I/O. `await` hands the thread back to the pool while the
database, broker or HTTP call is in flight, so a small pool serves many concurrent requests. The practices that
follow from that:

- **Async all the way down.** Every I/O call is awaited: `ToListAsync`, `SaveChangesAsync`, `QueryAsync`,
  `HttpClient.SendAsync`. Never `.Result`, `.Wait()` or `GetAwaiter().GetResult()`: they block a pool thread, and
  under load that is how thread-pool starvation starts.
- **Pass the `CancellationToken` through.** ASP.NET Core cancels `HttpContext.RequestAborted` when the client goes
  away. Every service method here takes `CancellationToken ct` and hands it to EF, Dapper and MassTransit, so
  abandoned requests stop using the database.
- **No `async void`** except event handlers. Exceptions in it crash the process and can't be awaited.
- **No `ConfigureAwait(false)` in app code.** ASP.NET Core has no synchronization context, so it buys nothing
  (CA2007 is suppressed for that reason in [Directory.Build.props](Directory.Build.props)). It still matters in
  libraries that might run under one.
- **Run independent I/O concurrently** with `Task.WhenAll`, but never on the same `DbContext`: a context isn't
  thread-safe, so concurrent queries need separate contexts.
- **`ValueTask`** only on hot paths that usually complete synchronously, and never awaited twice.

**`Task.Run` in a web API: almost never.** It moves work from one pool thread to another, so the request still
waits and you've added a thread hop. Wrapping synchronous I/O in it ("fake async") just moves the blocking. The
legitimate cases are narrow: CPU-heavy work you must do in-process and want off the request thread *and* can
afford to bound (better: a queue and a background worker, which is what this project does for allocation), or an
unavoidable synchronous library call at start-up. For background work that outlives the request, use a
`BackgroundService` or a queue, not fire-and-forget `Task.Run`, which loses exceptions and dies with the request
scope's services.

### 2. Minimal APIs vs controllers

> Minimal APIs vs controller-based: pros/cons and when you prefer each.

| | Minimal APIs | Controllers |
|---|---|---|
| Ceremony | Very little: a route and a lambda | Classes, attributes, base class |
| Performance | Slightly faster (no MVC filter pipeline), AOT-friendly | Marginally slower; rarely the bottleneck |
| Cross-cutting | Endpoint filters and route groups | MVC filters, model binding, conventions, `[ApiController]` behaviours |
| Organisation | Easy to sprawl in `Program.cs` unless you impose structure | The structure is the convention |
| OpenAPI | Good in .NET 8, more explicit metadata (`.Produces<T>()`) | Mature (Swashbuckle, XML comments, `[ProducesResponseType]`) |

**I prefer Minimal APIs** for small services, microservices with a handful of endpoints, and anything aiming for
Native AOT. **I prefer controllers** for a business API with many endpoints, shared filters and a team that
benefits from one obvious place for each thing. This project uses controllers: versioned routes
(`Asp.Versioning`), a global FluentValidation action filter, `[Authorize(Policy = ...)]` and rate-limit attributes
per action, and XML-comment-driven Swagger, all of which controllers make uniform with no extra plumbing (see
[OrdersController.cs](src/OrderManagement.Api/Controllers/V1/OrdersController.cs)). Health endpoints, which are
tiny, are mapped minimal-style. Either way the controller or handler should be thin: the logic lives in a service
and the domain.

### 3. Tracking vs AsNoTracking and optimistic concurrency

> Tracking vs `AsNoTracking()`. How to implement optimistic concurrency safely?

**Tracking** keeps a snapshot of every loaded entity so `SaveChanges` can detect changes and write them. That
costs memory and CPU for every row, and identity resolution on every query. **`AsNoTracking`** skips it: right for
any read that won't be saved, which is most of them. This project reads with `AsNoTracking` or a `Select`
projection straight into the response DTO (no entity materialised at all), and only tracks when it's about to
write (see [OrderService.cs](src/OrderManagement.Api/Services/OrderService.cs): the list, get and ETag queries are
no-tracking; the status change loads tracked). Projections also fetch only the columns the response needs, which
lets SQL Server answer the list from a covering index ([Q14](#14-indexing)).

**Optimistic concurrency** assumes conflicts are rare and detects them at write time instead of holding locks:

1. A `rowversion` column on `Orders`, mapped with `IsRowVersion()`. SQL Server bumps it on every update; EF adds
   `WHERE Id = @id AND RowVersion = @original` to the `UPDATE`.
2. If another writer got there first, zero rows are affected and EF throws `DbUpdateConcurrencyException`.
3. Handle it deliberately. Here that means: clear the change tracker, see whether the winner was this same
   idempotent request (replay its result), otherwise return **409** with a friendly ProblemDetails so the client
   reloads. Never "retry until it works" blindly: re-evaluate the business rule against the fresh state.
4. Expose the token to clients. The rowversion is the `ETag`; a client that sends `If-Match` with a stale ETag gets
   **412** before any change is attempted. That catches the "lost update" across a user's think-time, which the
   database check alone (load → save in one request) doesn't.

The full code is in [Q16](#16-optimistic-concurrency-with-rowversion).

### 4. Where business rules live

> Where do you enforce business rules (DTO vs domain vs DB)? Would you use FluentValidation or manual validation,
> and why?

All three, each for a different job, and none duplicating another's purpose:

- **Request/DTO (FluentValidation):** the *shape* of the input. Required fields, lengths, formats, ranges, page
  size ≤ 100. It fails fast with field-level, human messages before any I/O. See
  [OrderValidators.cs](src/OrderManagement.Api/Contracts/Orders/OrderValidators.cs).
- **Domain (the aggregate):** the *business invariants*, which must hold however the object is reached: the
  currency must be permitted for the customer's country, a price can't have more decimals than its currency
  allows, a SKU may appear once per order, only legal status transitions, the total is computed, never supplied.
  `Order.Create` and `Order.TransitionTo` return `ErrorOr`, reporting every problem at once. See
  [Order.cs](src/OrderManagement.Domain/Orders/Order.cs).
- **Database:** the *last line of defence* that also protects against other writers, bugs and manual SQL:
  `CHECK (Quantity > 0)`, `CHECK (UnitPrice >= 0)`, `CHECK (Status IN (...))`, the unique email index, foreign keys
  with `Restrict`/`Cascade`, `decimal(18,2)`. A violation here is a bug, so it surfaces as a 500 (or a mapped 409
  for the unique email race), not a validation message.

**FluentValidation over manual checks** for the request layer: the rules are declarative and testable in
isolation, the messages live next to the rules, `RuleForEach` handles collections (line items) with indexed paths
(`lineItems[0].quantity`) that the UI maps onto fields, and a single global filter applies it to every action so
nobody forgets. I would *not* put business rules in validators (they'd need the database, and they'd bypass the
domain when the worker changes an order). Manual validation is fine for the domain, where the rules are code in
the aggregate itself.

### 5. Global exception handling and ProblemDetails

> Implementing global handlers and Problem Details (RFC 7807); what should error contracts look like?

Two kinds of failure, two paths:

- **Expected failures** (not found, validation, invalid transition, conflict) are not exceptions here. Services
  return `ErrorOr` values and one mapper turns each error type into a status and a ProblemDetails
  ([ErrorOrProblems.cs](src/OrderManagement.Api/Common/Errors/ErrorOrProblems.cs)).
- **Unexpected exceptions** go to a single `IExceptionHandler`
  ([GlobalExceptionHandler.cs](src/OrderManagement.Api/Common/Errors/GlobalExceptionHandler.cs)) registered with
  `AddExceptionHandler` + `UseExceptionHandler`. It logs the full exception with the correlation id and returns a
  generic 500. **No stack trace, exception type or SQL ever reaches the client.** A client cancellation is not
  logged as an error.

Every ProblemDetails, wherever it comes from (our mapper, MVC model binding, auth, routing, API versioning, the
exception handler), passes through one enricher via `AddProblemDetails(CustomizeProblemDetails)`, so the contract
is uniform:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "That change conflicts with the current state",
  "status": 409,
  "detail": "A customer with the email address lerato@example.co.ls already exists.",
  "instance": "/api/v1/customers",
  "code": "email_already_exists",
  "errors": { "email": ["..."] },
  "traceId": "00-9a7256c4fceb742ec3b8aa17dd315d30-7226835f65adfb73-01",
  "correlationId": "9a7256c4fceb742ec3b8aa17dd315d30"
}
```

What makes a contract good: the **standard fields** so generic tooling works; a **stable machine `code`** clients
can branch on (never parse `title`); **plain-English `title` and `detail`** written for the person reading them,
replacing framework boilerplate like "One or more validation errors occurred"; **field errors keyed by the JSON
path** so a UI can highlight the field; a **correlation id** to quote to support; and the correct **status**
(400 malformed, 401/403 auth, 404, 409 state conflict, 412 stale ETag, 422 reused idempotency key, 429, 500). The
`application/problem+json` content type tells clients it's an error body.

### 6. REST search endpoints, GraphQL and N+1

> REST search endpoints: query params, pagination metadata, sorting; when is GraphQL preferable; N+1 pitfalls.

**Query parameters** are the filter, in plain names: `GET /api/orders?customerId=&status=&page=1&pageSize=20&sort=-total`.
Rules I apply (all implemented):

- **Bound the page size** (≤ 100 here) and default it, so no client can ask for the whole table.
- **Whitelist sort fields** (`createdAt`, `total`), with `-` for descending. Never interpolate a client string into
  `ORDER BY`. Always add a unique tiebreaker (`Id`) so pages are stable and rows don't repeat or vanish between
  pages.
- **Return pagination metadata in the body:** `items`, `page`, `pageSize`, `totalCount`, `totalPages`,
  `hasPrevious`, `hasNext`, so the UI can render paging without arithmetic.
- **Validate filters** with friendly 400s, case-insensitive enums, and a `search` that is *starts with*
  (index-friendly `LIKE 'abc%'`) rather than contains.
- For very large or fast-moving sets, **keyset (cursor) pagination** (`WHERE (CreatedAt, Id) < (@lastCreatedAt,
  @lastId)`) beats `OFFSET`, which scans and discards every skipped row and shifts when rows are inserted.

**GraphQL is preferable** when many clients need different shapes of the same graph (mobile vs web vs partners),
when screens need nested data from several resources in one round trip, or when over-fetching is a real cost. It
costs you HTTP caching (one POST endpoint), simple per-endpoint authorization and rate limiting, and it makes
query cost harder to bound (you need depth/complexity limits and persisted queries). For this API, with one UI
and stable shapes, REST with ETags is the main API. The brief's optional read-only GraphQL endpoint exists alongside it
(`/graphql`, orders by customer with nested line items) for clients that want to shape their own reads.

**N+1:** loading a list, then lazily touching a navigation per row (`order.LineItems` in a loop), issues one query
per row. Avoid it with explicit **`Include`** for the aggregate you need (`GET /orders/{id}` loads the order and
its lines in one query), **projection** for lists (the order list selects `CustomerName` through the join in the
same query, never a per-row customer lookup), **no lazy-loading proxies**, and `AsSplitQuery` when an `Include`
of several collections would explode the row count. In GraphQL the same problem appears per resolver and is
solved with DataLoader batching, or, as this project's `/graphql` does, by returning an `IQueryable` that Hot
Chocolate projects, so orders and their line items come back in one SQL query (a test counts the commands).

### 7. Entra JWT validation and policies

> Validating JWT via Microsoft.Identity.Web, mapping roles/claims, token lifetimes, and policy-based authorization.

**Validation.** `AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"))` configures JWT bearer validation
from the tenant's OpenID metadata: it downloads and caches the signing keys (and rotates with them), and validates
the **signature**, **issuer** (the tenant), **audience** (this API's app ID URI or client ID), and **lifetime**
(`exp`/`nbf`, with a small clock skew). An API must never accept a token issued to someone else, so the audience
check is non-negotiable. See [AuthenticationSetup.cs](src/OrderManagement.Api/Auth/AuthenticationSetup.cs).

**Roles and claims.** App roles (`Orders.Read`, `Orders.Write`, `Orders.Admin`) are defined on the Entra app
registration and assigned to users, groups or (for daemon clients) applications; they arrive in the `roles`
claim. Map it as the role claim type so `RequireRole` works. User identity is the `oid` claim (stable, unique per
tenant), not `email` or `name`, which can change. Scopes (`scp`) are for delegated permissions; app roles suit an
internal line-of-business API.

**Policies, not roles in attributes.** Controllers say `[Authorize(Policy = AuthPolicies.OrdersWrite)]`; the policy
decides which roles satisfy it. Here roles are hierarchical (Admin ⊇ Write ⊇ Read), so changing that mapping is a
one-line change in [AuthPolicies.cs](src/OrderManagement.Api/Auth/AuthPolicies.cs), not a search through
attributes. A **fallback policy** requires an authenticated user everywhere, so a new endpoint without an
attribute is closed by default; public endpoints (health, the dev token) opt out explicitly with
`[AllowAnonymous]`.

**Token lifetimes.** Entra access tokens live 60–90 minutes by default and can't be revoked mid-life (continuous
access evaluation narrows this for supported clients). So keep them short, keep authorization decisions in the API
on every request, let the client (MSAL) refresh silently with refresh tokens, and treat a 401 as "get a new token".
The web app here clears its cached token on 401.

**Without a tenant,** this project runs the same pipeline against a mock issuer: tokens shaped exactly like Entra's
(`roles`, `oid`, `aud`, `iss`), signed with a local key, minted by `POST /api/v1/dev/token` only in Development and
Testing. The API refuses to start in Production in mock mode. Integration tests use it to prove 401 and 403 per
endpoint.

### 8. RabbitMQ: exchanges, durability, delivery and idempotency

> Exchange types, queue durability, at-least-once delivery, and designing idempotency (keys/dedup).

**Exchange types:**

- **Direct:** routes on an exact routing key. Point-to-point work queues.
- **Topic:** wildcard patterns (`orders.*.created`, `orders.#`). Selective pub/sub.
- **Fanout:** every bound queue gets every message, regardless of key. Broadcast events.
- **Headers:** routes on message headers instead of the key. Rarely needed.

MassTransit (used here) maps each message type to a fanout exchange and each consumer endpoint to its own queue
bound to it, so `OrderCreated` can gain new subscribers without touching the publisher.

**Durability** needs three things together: a **durable** exchange and queue (survive a broker restart),
**persistent** messages (delivery mode 2, written to disk), and **publisher confirms**, so the publisher knows the
broker has the message. In production, **quorum queues** replicate across nodes. MassTransit declares durable
topology and uses confirms by default.

**At-least-once delivery** is the realistic guarantee. A message is removed only when the consumer **acks** after
processing, so a crash mid-processing redelivers it. The consequence: **every consumer will occasionally see a
message twice**, and the publisher side has its own gap, the dual write (save the order, then publish: a crash in
between loses the event). This project closes both:

- **Transactional outbox:** the `OrderCreated` message is written to `OutboxMessage` in the same SQL transaction
  as the order, and a delivery service publishes it afterwards. Order and event commit together or not at all.
- **Inbox de-duplication:** the consumer side records each `MessageId` in `InboxState` within the consumer's
  transaction, so a redelivered message is skipped.
- **Idempotent handlers anyway:** `OrderCreatedConsumer` returns early if the order is already allocated or
  cancelled ([OrderCreatedConsumer.cs](src/OrderManagement.Worker/Consumers/OrderCreatedConsumer.cs)), because
  dedup windows expire and state is the real truth.
- **Retry, then dead-letter:** exponential retry (5 attempts, 1 s growing to 30 s) for transient faults, then the
  message moves to `order-created_error`, where it can be inspected and replayed, instead of blocking the queue or
  being lost.

**Idempotency keys at the HTTP edge** follow the same idea: `PUT /orders/{id}/status` requires an
`Idempotency-Key`. The key, a hash of the request and the stored response are saved **in the same transaction as
the status change**. A retry with the same key replays the stored response; the same key with a different body
returns 422; keys expire after 24 hours. A concurrent duplicate loses the primary-key race and replays the
winner's response.

### 9. Frontend state and type-safe contracts

> State management choices (Context vs Redux Toolkit vs SWR/RTK Query); how you keep API contracts type-safe.

Most "state" in a business app is **server state**: a cached copy of data the server owns. It needs caching,
de-duplication, background refetch, invalidation after writes and loading/error states, which is exactly what
**TanStack Query** (or SWR / RTK Query) provides. I keep the categories separate:

| State | Tool | In this app |
|---|---|---|
| Server data | TanStack Query | Customers, orders, countries, reports; mutations invalidate the affected lists; a paid order polls until the worker fulfils it |
| URL state | The router | Filters, sort and page live in the query string, so refresh and shared links work |
| Form state | react-hook-form + Zod | New customer / new order, with server errors mapped back onto fields |
| Global UI state | React Context | Theme and toasts: small, rarely changing |

**Context** is for low-frequency global values; using it for server data means hand-written caching and
re-rendering every consumer on every change. **Redux Toolkit** earns its place with complex client-side state
(offline editing, multi-step workflows, undo) or an existing Redux codebase; with **RTK Query** it's a fine choice
for server state too. For this app it would be ceremony.

**Type-safe contracts:** the API's OpenAPI document is the single source of truth. `openapi-typescript` generates
`schema.d.ts` from it and `openapi-fetch` makes every call typed by path, method, parameters and response. Changing
a response field breaks the web build, not production. I made the generator's output trustworthy at the source:
the API marks non-nullable response fields `required` and names query parameters in camelCase, so the types are
`id: string`, not `id?: string`. CI regenerates the types and fails if they differ from the committed file. Zod
validates *forms* (user input), not API responses, which the contract already types.

### 10. Test pyramid and CI gates

> Your test pyramid for this stack; what belongs in unit vs integration vs E2E; CI gates you would add (coverage,
> lint, security scan).

| Layer | Count here | What belongs there |
|---|---|---|
| **Unit** (xUnit + Shouldly) | 133 | The domain: money and currency rules, the SADC catalogue, the state machine, totals, allocation. Pure, fast, exhaustive on edge cases. |
| **Integration** (WebApplicationFactory + Testcontainers SQL Server) | 159 | Anything crossing a boundary: each endpoint through the real pipeline (auth, validation, ProblemDetails), real SQL (constraints, migrations, indexes in use), idempotent status updates including concurrent duplicates, the outbox actually writing `OrderCreated`, consumers against a real database, reports, GraphQL (including a no-N+1 check), the OpenAPI shape. |
| **Web component** (Vitest + RTL + MSW) | 47 | Forms and lists as a user sees them: query by role and label, assert on what's rendered and what was sent. |
| **E2E** (Playwright) | 17 | The few journeys that must never break, through the real stack: create customer → create order → pay → worker fulfils; cancel; validation. Plus an axe WCAG scan of every page. |

Integration tests are the most valuable layer for an API like this, so they're numerous; mocks of EF Core would
test the mocks. E2E stays small because it's the slowest and most brittle.

**CI gates** ([.github/workflows/ci.yml](.github/workflows/ci.yml)) on every pull request: build with **warnings
as errors** and analyzers on; **lint** (oxlint) and **typecheck**; all test suites; **contract drift** (generated
TS types vs the OpenAPI document; migration SQL vs the migrations); the **migration scripts applied, re-applied,
rolled back and re-applied on an empty SQL Server**; **E2E against the Docker Compose stack**; images published
only from `master` after all of that passes. What I'd add next: **coverage** reporting with a ratchet (no drop
below the current figure) rather than an arbitrary target; **dependency and secret scanning** (Dependabot,
`dotnet list package --vulnerable`, `npm audit` at high severity, GitHub secret scanning); **CodeQL** static
analysis; and an **image scan** (Trivy) before publishing.

### 11. Performance, scalability and observability

> Handling write spikes (queues/backpressure), indexing, caching, correlation IDs and tracing, SLOs.

**Write spikes.** The synchronous path does as little as possible: validate, one transaction (order + lines +
outbox row), return 201. Everything slow (allocation, fulfilment) happens behind the broker, so a spike fills a
queue instead of exhausting the API. **Backpressure** comes from the consumer's prefetch and concurrency limits:
the worker takes only what it can process, and the queue absorbs the rest. At the edge, **rate limiting** returns
429 instead of collapsing: every request counts against 600 per minute per user (keyed on the token's `oid`, or the
IP address when anonymous), and writes against a stricter 120; both are configurable. Beyond that, the next step
for extreme spikes is queue-first ingestion: accept the request into a queue, return 202 with a status URL, and
create the order asynchronously. It trades immediate consistency for absorbing almost any burst. I chose 201
because orders here must be readable immediately.

**Indexing.** Indexes follow the queries: `IX_Orders_CustomerId_Status_CreatedAt INCLUDE (TotalAmount,
CurrencyCode)` covers the customer's order list; `IX_Orders_Status_CreatedAt` covers the all-customers list;
`IX_Customers_CountryCode INCLUDE (Name)` serves the report; unique `Email`. Each extra index slows writes, so
add them from real plans, not guesses ([Q14](#14-indexing)).

**Caching.** `GET /orders/{id}` returns an `ETag` (the rowversion) with `Cache-Control: private, no-cache`; a
client revalidating with `If-None-Match` gets a 304, and the API answers that by reading only the rowversion, not
the line items. Reference data (countries and currencies) is cached on the client for the session. I'd add a
server cache (output cache or Redis) only for hot, read-heavy, tolerably-stale data, with explicit invalidation;
caching order state risks serving stale status. The Redis `HybridCache` wiring is sketched, commented out, in
`ApiSetup.cs`.

**Correlation and tracing.** Every request gets an `X-Correlation-ID` (accepted from the client or generated),
returned in the response, written on every log line, included in every ProblemDetails, and **carried in message
headers to the worker**, so one id ties API logs, broker messages and worker logs together. OpenTelemetry traces
propagate the same way (W3C `traceparent`), so the Aspire dashboard shows one trace from HTTP request through SQL
and RabbitMQ to the worker's SQL. Custom metrics count orders created by currency, status changes, allocations and
fulfilments.

**SLOs** I'd set for this service, measured from those metrics: availability 99.9% of `/api` requests non-5xx
over 30 days; latency p95 < 300 ms for reads and < 500 ms for order creation; **freshness** 99% of paid orders
fulfilled within 60 s (the asynchronous path needs its own SLO, from queue depth and consumer lag, or a stuck worker
goes unnoticed while the API looks healthy). Alert on error-budget burn rate, not on single spikes.

---

## SQL section

### 12. Pagination query

> Return a customer's orders with status, createdAt, totalAmount, sorted by createdAt DESC, and a separate
> COUNT(*) for the total. Show how you'd pass @CustomerId, @Offset, @PageSize.

```sql
-- Page of orders
SELECT o.Id, o.Status, o.CreatedAt, o.TotalAmount, o.CurrencyCode
FROM Orders AS o
WHERE o.CustomerId = @CustomerId
ORDER BY o.CreatedAt DESC, o.Id DESC          -- Id breaks ties, so pages are stable
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

-- Total for the pagination metadata
SELECT COUNT(*) AS TotalCount
FROM Orders
WHERE CustomerId = @CustomerId;
```

Parameters are always parameters, never concatenated strings:

```csharp
const int maxPageSize = 100;
var pageSize = Math.Clamp(request.PageSize, 1, maxPageSize);
var offset = (Math.Max(request.Page, 1) - 1) * pageSize;

var args = new { CustomerId = customerId, Offset = offset, PageSize = pageSize };
var items = await connection.QueryAsync<OrderRow>(new CommandDefinition(PageSql, args, cancellationToken: ct));
var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(CountSql, args, cancellationToken: ct));
```

In ADO.NET terms that's `cmd.Parameters.Add("@CustomerId", SqlDbType.UniqueIdentifier).Value = customerId` and
`SqlDbType.Int` for the other two. Notes:

- `IX_Orders_CustomerId_Status_CreatedAt INCLUDE (TotalAmount, CurrencyCode)` serves both statements from the
  index alone (the `CustomerId` seek; the sort is cheap because one customer's orders are few, and adding
  `Status` to the filter makes it an ordered range).
- The API does the same thing in LINQ (`Skip`/`Take` on a projection plus `CountAsync`), which EF translates to
  exactly this `OFFSET … FETCH` shape.
- Both statements can run in one round trip (two result sets via `QueryMultiple`), or the count can come from
  `COUNT(*) OVER ()` in the page query, at the cost of computing it per row.
- Deep pages make `OFFSET` scan and discard rows; for those, keyset pagination (`WHERE (CreatedAt < @lastCreatedAt)
  OR (CreatedAt = @lastCreatedAt AND Id < @lastId)`) is constant-time.

### 13. Top spenders (last 90 days)

> Return the top 10 customers by total spend over the last 90 days. Include customers with zero orders
> (show total = 0). Use LEFT JOIN, COALESCE, and date filters.

This is the query the API runs (`GET /api/v1/reports/top-spenders`,
[OrderReportQueries.cs](src/OrderManagement.Infrastructure/Reports/OrderReportQueries.cs)):

```sql
DECLARE @Since datetime2 = DATEADD(DAY, -90, SYSUTCDATETIME());

SELECT TOP (10)
       c.Id                            AS CustomerId,
       c.Name,
       c.CountryCode,
       COALESCE(SUM(o.TotalAmount), 0) AS TotalSpend,
       COUNT(o.Id)                     AS OrderCount
FROM Customers AS c
LEFT JOIN Orders AS o
       ON  o.CustomerId   = c.Id
       AND o.CurrencyCode = @Currency                 -- never sum across currencies
       AND o.Status IN ('Paid', 'Fulfilled')          -- money actually committed
       AND o.CreatedAt   >= @Since
GROUP BY c.Id, c.Name, c.CountryCode
ORDER BY TotalSpend DESC, c.Name, c.Id;
```

The details that make it correct:

- **The order filters are in the `ON` clause, not `WHERE`.** A `WHERE o.CreatedAt >= @Since` would reject the
  NULL-extended rows the LEFT JOIN produced for customers without orders, quietly turning it into an inner join and
  dropping exactly the zero-spend customers the question asks for. An integration test asserts those customers
  appear at 0, and moving the filters into `WHERE` makes it fail (checked by mutation when the query was written).
- **`COALESCE(SUM(...), 0)`** turns those customers' NULL sum into 0, and `COUNT(o.Id)` (not `COUNT(*)`) counts 0
  orders for them, not 1.
- **One currency per report.** Summing ZAR with BWP is meaningless, so the currency is a parameter. The API also
  restricts customers to countries that may order in that currency (for ZAR: South Africa plus the CMA countries),
  so a ZAR report doesn't list every Botswanan customer at 0.
- **Only Paid and Fulfilled count.** Pending orders aren't spend yet, and Cancelled never will be.
- **The date is computed in UTC** (`SYSUTCDATETIME()`), matching how `CreatedAt` is stored. The cut-off is a
  parameter so the clause stays sargable.
- **Deterministic ordering:** ties on spend break by name and id, so `TOP (10)` always returns the same ten.

### 14. Indexing

> Propose indexes for frequent reads of Orders by (CustomerId, Status, CreatedAt). Explain the benefit and
> trade-offs of a covering index for listing queries vs clustered index choice.

```sql
CREATE INDEX IX_Orders_CustomerId_Status_CreatedAt
    ON Orders (CustomerId, Status, CreatedAt)
    INCLUDE (TotalAmount, CurrencyCode);

-- For listing across all customers by status (the admin view)
CREATE INDEX IX_Orders_Status_CreatedAt
    ON Orders (Status, CreatedAt)
    INCLUDE (CustomerId, TotalAmount, CurrencyCode);
```

Both exist in this schema (migration `InitialCreate`).

**Key order** follows how the queries filter: equality columns first (`CustomerId`, then `Status`), the range or
sort column last (`CreatedAt`). Then `WHERE CustomerId = @c AND Status = @s ORDER BY CreatedAt DESC` is a single
seek to one contiguous range, already in order: no sort operator, and `TOP`/`OFFSET` can stop early. A query with
only `CustomerId` still seeks on the leading column.

**Covering (`INCLUDE`)** puts the list's other columns in the index leaf, so the query never visits the base table.
Without it, every row found costs a **key lookup** into the clustered index: one random read per row, which for a
page of 100 is 100 extra lookups, and past a few thousand rows the optimizer gives up and scans the table instead.
`INCLUDE` columns aren't in the key, so they don't affect seeks or sort order and don't bloat the upper levels.

**The trade-offs of covering:** every write updates every index that contains a changed column, so each index
costs insert/update time, log volume, memory and storage. Cover the hot queries, not every query. And don't
include volatile columns: `Status` changes, but it's needed as a key column anyway; `RowVersion` changes on every
update, so it isn't included.

**Clustered index choice:** the clustered index *is* the table, ordered by its key, and every nonclustered index
carries that key as its row locator, so it should be narrow, unique, static and ideally ever-increasing.

- **A random GUID** key fragments the clustered index (inserts land on random pages, causing page splits) and
  makes every nonclustered index 16 bytes wider per row.
- **This project uses sequential GUIDs** (EF's SQL Server value generator), which keep inserts appending at the
  end while still giving globally unique, client-safe ids.
- **The alternative** is a clustered key on `(CustomerId, CreatedAt, Id)`, which makes per-customer range reads
  very cheap without any includes. The cost is inserts scattered across the table (one hot spot per customer), and
  a key that changes if a customer is ever merged.

For an orders table that's inserted constantly and read per customer, I prefer an append-only clustered key plus
covering nonclustered indexes.

### 15. Execution plans and key lookups

> Given a join between Orders and OrderLineItems with filters on CustomerId and Status, explain how to detect a
> key lookup in the plan and how you'd remove it (e.g., include columns in an index).

```sql
SELECT o.Id, o.CreatedAt, o.TotalAmount, li.ProductSku, li.Quantity, li.UnitPrice
FROM Orders AS o
JOIN OrderLineItems AS li ON li.OrderId = o.Id
WHERE o.CustomerId = @CustomerId AND o.Status = @Status;
```

**Detecting it:**

- Run with the **actual** execution plan (SSMS "Include Actual Execution Plan", or `SET STATISTICS XML ON`).
- A key lookup shows as a **Key Lookup (Clustered)** operator (a *RID Lookup* on a heap), fed by a **Nested Loops**
  join from an **Index Seek**. Its tooltip's **Output List** names the columns the seek's index didn't have, and
  **Number of Executions** equals the rows the seek returned, so a high number means one random read per row.
- `SET STATISTICS IO ON` shows the same thing as high logical reads on the table.
- Query Store and `sys.dm_exec_query_stats` find the costly plans in production.
- A seek followed by a lookup on thousands of rows, or the optimizer *abandoning* the index for a clustered index
  scan, are the signatures.

**Removing it:**

- **On Orders:** the seek on `IX_Orders_CustomerId_Status_CreatedAt` needs `TotalAmount` (and `Id`, which every
  nonclustered index carries as the clustered key). Adding `TotalAmount` to `INCLUDE`, as this schema already
  does, makes the Orders side covering: no lookup.
- **On OrderLineItems:** the join seeks by `OrderId`. The unique index `IX_OrderLineItems_OrderId_ProductSku`
  provides `OrderId` and `ProductSku`, but `Quantity` and `UnitPrice` need a lookup. Fix it with
  `INCLUDE (Quantity, UnitPrice)` on that index, or a covering index `ON OrderLineItems (OrderId) INCLUDE
  (ProductSku, Quantity, UnitPrice)`.
- **Or select fewer columns.** Often the lookup exists only because the query asks for `SELECT *`. Projecting just
  the needed columns, as the API does, is the cheapest fix of all.

Re-run and confirm: the Key Lookup operator is gone and logical reads drop. Only cover queries that are actually
hot, because each included column is paid for on every write ([Q14](#14-indexing)).

### 16. Optimistic concurrency with rowversion

> Show how to add a rowversion column for Orders and handle DbUpdateConcurrencyException during an update.
> Include EF Core code snippet and brief explanation.

**The column.** The entity property and its mapping:

```csharp
public sealed class Order
{
    // ...
    public byte[] RowVersion { get; private set; } = [];
}

// OrderConfiguration : IEntityTypeConfiguration<Order>
builder.Property(o => o.RowVersion).IsRowVersion();
```

`dotnet ef migrations add AddOrderRowVersion` produces `ALTER TABLE [Orders] ADD [RowVersion] rowversion NOT
NULL;`, which is an additive, online change: SQL Server fills the value for existing rows. This project's migration
of that name is exactly this.

**The update.** EF now adds the original rowversion to the `UPDATE`'s `WHERE` clause. If another transaction
changed the row in between, no row matches and EF throws. A simplified version of this project's
`OrderService.ChangeStatusAsync` (the real one also records the idempotency key in the same transaction):

```csharp
public async Task<ErrorOr<OrderResponse>> ChangeStatusAsync(Guid id, OrderStatus target, string? ifMatch, CancellationToken ct)
{
    var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id, ct);
    if (order is null) return OrderErrors.NotFound(id);

    // The client's view is stale (If-Match carries the ETag it saw): 412 before changing anything.
    if (ifMatch is not null && ifMatch != ETags.From(order.RowVersion)) return OrderErrors.PreconditionFailed;

    var transition = order.TransitionTo(target);          // domain rule: legal transitions only
    if (transition.IsError) return transition.Errors;

    try
    {
        await db.SaveChangesAsync(ct);                    // UPDATE ... WHERE Id = @id AND RowVersion = @original
        return ToResponse(order);                         // new RowVersion → new ETag for the client
    }
    catch (DbUpdateConcurrencyException)
    {
        // Someone changed the order between our read and our write. Don't overwrite their change, and don't
        // blindly retry: report a conflict with the current state so the caller can decide.
        db.ChangeTracker.Clear();
        var current = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == id, ct);
        return OrderErrors.ConcurrencyConflict(current.Status);   // → 409 ProblemDetails
    }
}
```

**How it fits together.** `rowversion` is an 8-byte counter SQL Server increments on every update to the row, so it
is a perfect, cheap version stamp. It doesn't need a trigger or application discipline. The API exposes it as the
`ETag`, which gives two levels of protection:

- `If-Match` (412) catches changes made while a *user* was looking at the page.
- The `WHERE RowVersion = @original` check (`DbUpdateConcurrencyException`, 409) catches changes made in the
  milliseconds *between this request's read and write*.

In this codebase, the catch also checks whether the "winner" was the same idempotent request retried by the client,
and replays its result instead of reporting a conflict
([OrderService.cs](src/OrderManagement.Api/Services/OrderService.cs), `ChangeStatusAsync`). The worker's updates
use the same token; on a conflict, MassTransit's retry re-reads the order and re-applies the rule to the fresh
state.

### 17. Deadlocks

> Describe a reader/writer deadlock scenario on Orders; show one mitigation (e.g., consistent index/order, shorter
> transactions, READ COMMITTED SNAPSHOT, or specific locking hints only when justified).

**The scenario.** Under SQL Server's default `READ COMMITTED` (locking), readers take shared (S) locks and writers
exclusive (X) locks, so a reader and a writer that touch the same rows through **different indexes in opposite
order** can deadlock:

1. **Writer** (the worker): `UPDATE Orders SET Status = 'Fulfilled' WHERE Id = @id`. It takes an X lock on the row
   in the clustered index, then needs to update the nonclustered `IX_Orders_CustomerId_Status_CreatedAt` (because
   `Status` is a key column there), so it requests an X lock on that index row.
2. **Reader** (the customer's order list) meanwhile seeks `IX_Orders_CustomerId_Status_CreatedAt` and holds an S
   lock on that same index row. The list needs a column the index doesn't have, so it requests an S lock on the
   clustered row to do a key lookup.
3. The writer waits for the reader's S lock on the index row; the reader waits for the writer's X lock on the
   clustered row. That's a cycle. SQL Server picks a victim (usually the cheaper reader) and kills it with error
   **1205**.

**Mitigation 1: READ COMMITTED SNAPSHOT (RCSI), which I'd turn on:**

```sql
ALTER DATABASE OrderManagement SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
```

With RCSI, readers read the last committed version of each row from the version store in tempdb and take **no
shared locks**, so a reader can never block or deadlock with a writer. Writers still lock each other, which is
correct. It is the default in Azure SQL Database. The costs:

- tempdb space and IO for row versions;
- 14 bytes added to each modified row;
- readers may see data that is a few milliseconds old, which is what read committed promises anyway.

Code that relied on readers blocking (a "check then insert" pattern) must use proper constraints or `UPDLOCK`
instead.

**Mitigation 2, supporting it:** remove the key lookup by covering the list query (`INCLUDE (TotalAmount,
CurrencyCode)`, already in place). The reader then touches only the nonclustered index, which takes the second
resource out of the cycle.

**Mitigation 3, general hygiene:**

- **Keep transactions short:** no user think-time, HTTP calls or message publishing inside them. The outbox makes
  publishing part of the commit instead of holding locks across a network call.
- **Access tables and rows in a consistent order** in multi-statement transactions.
- **Retry on 1205:** EF's `EnableRetryOnFailure` execution strategy treats deadlock victims as transient, which
  this project enables.

**Locking hints only when justified:**

- `NOLOCK` is *not* a fix. It reads uncommitted and even duplicated or missing rows.
- `UPDLOCK, HOLDLOCK` on a read that precedes an update in the same transaction is justified. It serializes the
  read-modify-write that would otherwise convert S locks to X, the classic conversion deadlock.

### 18. Window functions: running total

> Write a query to compute the running total of order spend per customer ordered by CreatedAt using
> SUM() OVER (PARTITION BY CustomerId ORDER BY CreatedAt).

```sql
SELECT o.CustomerId,
       o.Id           AS OrderId,
       o.CreatedAt,
       o.TotalAmount,
       SUM(o.TotalAmount) OVER (
           PARTITION BY o.CustomerId, o.CurrencyCode         -- one running total per customer per currency
           ORDER BY o.CreatedAt, o.Id
           ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
       ) AS RunningTotal
FROM Orders AS o
WHERE o.Status IN ('Paid', 'Fulfilled')
ORDER BY o.CustomerId, o.CurrencyCode, o.CreatedAt, o.Id;
```

The API runs the single-customer form (`GET /api/v1/reports/customers/{id}/running-totals?currency=`), which
filters `CustomerId` and `CurrencyCode` instead of partitioning by them. Details worth getting right:

- **`ROWS`, not the default `RANGE`.** With only `ORDER BY`, the default frame is `RANGE UNBOUNDED PRECEDING`, which
  treats orders with the *same* `CreatedAt` as peers and gives them all the combined total. `ROWS` advances one row
  at a time, and SQL Server can use the faster in-memory window spool instead of the on-disk one.
- **A tiebreaker (`Id`) in the `ORDER BY`** makes the order of same-instant orders, and so the running total,
  deterministic.
- **Partition (or filter) by currency.** A running total across ZAR and USD orders is meaningless.
- **Only committed spend:** Paid and Fulfilled. Swap the status filter for a definition that fits the report.
- An index on `(CustomerId, CreatedAt) INCLUDE (TotalAmount, CurrencyCode, Status)` lets the window be computed
  from an ordered scan with no sort.

### 19. Partitioning strategy

> Propose a partitioning scheme (by month or by CreatedAt range) for millions of Orders, and discuss partition
> switching for archival.

**The scheme: monthly range partitions on `CreatedAt`.** Orders are written once at a known time, almost all reads
and changes target recent months, and archival is by age, so time is the natural partitioning key.

```sql
-- One boundary per month. RANGE RIGHT: each boundary is the first instant of its month.
CREATE PARTITION FUNCTION pf_OrdersByMonth (datetime2)
    AS RANGE RIGHT FOR VALUES ('2026-01-01', '2026-02-01', '2026-03-01' /* … one per month, plus one empty future month */);

-- Map partitions to filegroups: hot months on fast storage, old ones on cheaper storage (or ALL TO PRIMARY).
CREATE PARTITION SCHEME ps_OrdersByMonth
    AS PARTITION pf_OrdersByMonth ALL TO ([PRIMARY]);

-- The clustered index must include the partitioning column; unique indexes must too.
CREATE TABLE Orders (
    Id           uniqueidentifier NOT NULL,
    CustomerId   uniqueidentifier NOT NULL,
    Status       varchar(16)      NOT NULL,
    CreatedAt    datetime2        NOT NULL,
    CurrencyCode char(3)          NOT NULL,
    TotalAmount  decimal(18,2)    NOT NULL,
    RowVersion   rowversion,
    CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (CreatedAt, Id) ON ps_OrdersByMonth (CreatedAt)
);

CREATE INDEX IX_Orders_CustomerId_Status_CreatedAt
    ON Orders (CustomerId, Status, CreatedAt) INCLUDE (TotalAmount, CurrencyCode)
    ON ps_OrdersByMonth (CreatedAt);                     -- aligned with the table
```

**Design consequences:**

- **The primary key becomes `(CreatedAt, Id)`,** because a unique index on a partitioned table must contain the
  partitioning column. Lookups by `Id` alone then probe every partition, so either the API passes `CreatedAt` too
  (it's in the order's ETag-carrying response), or a non-aligned unique index on `Id` keeps single-row lookups
  fast. The non-aligned option comes at the cost of blocking partition switching while it exists.
- **Keep every index aligned** (on the same scheme) so partitions can be switched.
- **`OrderLineItems` follows its order.** Either add `OrderCreatedAt` to it and partition it on the same function,
  so an order and its lines archive together, or archive lines in batches by `OrderId` before switching orders.
  Cross-partition foreign keys block switching, so the FK is usually dropped on the archive path and enforced on
  the live data.
- **Partition elimination** only happens when queries filter on `CreatedAt`. Customer queries ("my orders") should
  add a date window (the last 12 months by default) or they'll touch every partition, which is fine for a seek but
  worth knowing.

**Archival with partition switching.** Switching is a metadata-only operation: it moves a whole partition in or out
of the table in milliseconds, with no data copied and minimal logging.

```sql
-- 1. Staging table: identical columns, indexes and compression, on the same filegroup as the partition,
--    with a CHECK constraint matching the partition's range.
CREATE TABLE Orders_Archive_Stage ( /* identical definition */ ) ON [PRIMARY];
ALTER TABLE Orders_Archive_Stage ADD CONSTRAINT CK_Stage_Range
    CHECK (CreatedAt >= '2024-01-01' AND CreatedAt < '2024-02-01');

-- 2. Move the oldest month out of the live table: instant.
ALTER TABLE Orders SWITCH PARTITION $PARTITION.pf_OrdersByMonth('2024-01-01') TO Orders_Archive_Stage;

-- 3. Copy the staged month to the archive (a history table, a columnstore archive database, or export to cold
--    storage), then truncate or drop the staging table.

-- 4. Remove the now-empty boundary, and keep a spare empty month at the leading edge.
ALTER PARTITION FUNCTION pf_OrdersByMonth() MERGE RANGE ('2024-01-01');
ALTER PARTITION SCHEME ps_OrdersByMonth NEXT USED [PRIMARY];
ALTER PARTITION FUNCTION pf_OrdersByMonth() SPLIT RANGE ('2027-01-01');
```

**Operating it:**

- **Automate the sliding window as a scheduled job.** Each month it splits a new empty future partition (always
  before data arrives: splitting a non-empty partition moves rows and is slow), switches out the expired month, and
  merges the empty boundary.
- **Maintain per partition.** Index rebuilds can target a single partition (`REBUILD PARTITION = n`), and old
  partitions can use page or columnstore compression.
- **Scale up only when it's needed.** At "millions of rows", good indexes usually suffice. Partitioning pays off
  for manageability (instant archival, per-partition maintenance) more than for query speed. I'd introduce it when
  the table reaches hundreds of millions of rows, or when the retention policy makes bulk deletes painful.
