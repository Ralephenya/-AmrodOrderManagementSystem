# Answers

## General questions

### 1. async/await, and when to use Task.Run

When the API calls the database, it spends most of that time waiting. With `await`, the thread goes back to the pool while it waits and can serve other requests. If you block with `.Result` or `.Wait()`, the thread just sits there doing nothing, and under load you run out of threads.

So: use async all the way down (`ToListAsync`, `SaveChangesAsync`), never block on a task, and pass the `CancellationToken` through so the work stops if the user closes the page.

Example: 100 users load the orders page at the same time. With async, a handful of threads handle all of them while the database works. With `.Result`, you need 100 threads all sitting idle.

`Task.Run` in a web API: almost never. It just moves the work to another thread from the same pool, and the request still waits. If something is slow or heavy, put it on a queue and let a background worker do it. That's what this project does with stock allocation.

### 2. Minimal APIs vs controllers

Minimal APIs are a route and a function, with very little code around them. Controllers are classes with attributes, filters and a fixed structure.

Minimal APIs are great for small services with a few endpoints. Controllers are better for a bigger business API, because everyone knows where things go, and things like validation, auth policies and Swagger docs work the same way on every endpoint.

Example: this project uses controllers because it has many endpoints with versioning, auth policies and rate limits on each one. The health checks are tiny, so they're mapped minimal style.

### 3. Tracking vs AsNoTracking, and optimistic concurrency

With tracking, EF keeps a copy of everything it loads so it can work out what changed when you save. That costs memory and time. `AsNoTracking()` skips it, which is right for anything you're only going to read.

Example: the orders list uses `AsNoTracking()` because we only display it. Changing an order's status loads it with tracking because we're about to save it.

Optimistic concurrency means we don't lock the row. We check at save time whether someone else changed it first. The `Orders` table has a `rowversion` column that SQL Server changes on every update. EF adds `WHERE RowVersion = @old` to the update. If someone else saved in between, no row matches, EF throws `DbUpdateConcurrencyException`, and we return 409 so the user can reload. The code is in question 16.

### 4. Where business rules live

Each layer has its own job:

- The request (FluentValidation) checks the shape of the input: required fields, lengths, page size up to 100.
- The domain checks the business rules: is this currency allowed for this country, is this status change allowed.
- The database is the last safety net: foreign keys, `CHECK (Quantity > 0)`, a unique email.

Example: "quantity is empty" is caught by the validator. "Botswana customer paying in ZAR" is caught by the domain. A bug that somehow writes a negative quantity is stopped by the database.

I use FluentValidation for requests because the rules are easy to read, easy to test, and one filter applies them to every endpoint. Business rules stay in the domain, so they also apply when the worker changes an order, not only when a request comes in.

### 5. Global exception handling and ProblemDetails

Expected errors (not found, invalid input, wrong status change) aren't exceptions here. The service returns an error and we map it to the right status code. Unexpected crashes go to one global exception handler, which logs the full error and returns a plain 500. The client never sees a stack trace.

Every error uses the same ProblemDetails format, so the frontend handles them all the same way. Example:

```json
{
  "status": 409,
  "title": "That change conflicts with the current state",
  "detail": "A customer with this email already exists.",
  "code": "email_already_exists",
  "correlationId": "9a7256c4fceb742ec3b8aa17dd315d30"
}
```

A good error has the right status code, a message a human can read, a fixed `code` the app can check, and a correlation id the user can give to support.

### 6. REST search, pagination, GraphQL and N+1

Filters and paging go in the query string: `GET /api/orders?customerId=...&status=Paid&page=2&pageSize=20&sort=-total`. Page size is capped at 100, and only known sort fields are allowed. The response returns the items plus `page`, `pageSize` and `totalCount`, so the UI can draw the pager.

GraphQL is better when different clients need different shapes of the same data, like a mobile app wanting less than the web app. For one UI with fixed screens, REST is simpler and caches better. We still added a read-only GraphQL endpoint because the brief asked for one.

N+1 is when you load 20 orders and then run one more query per order to get its line items: 21 queries instead of 1. Fix it with `Include` or by selecting only what you need in one query. Example: the order details page loads the order and its line items in one query.

### 7. Entra JWT security

`Microsoft.Identity.Web` checks every token: it was signed by our tenant, it was issued for this API, and it hasn't expired.

Roles (`Orders.Read`, `Orders.Write`, `Orders.Admin`) are set up on the app registration in Entra and come through in the token's `roles` claim. Controllers use policies like `[Authorize(Policy = "Orders.Write")]` instead of checking roles directly, so if the rules change, we change one place. Every endpoint needs a logged-in user by default, so a new endpoint can't be left open by accident.

Tokens last about an hour and can't be cancelled early, so keep them short and let the frontend get a new one silently when it expires.

Example: a user with only `Orders.Read` can view orders, but gets 403 when trying to change a status.

We don't have a real tenant, so the project uses a mock token issuer that creates tokens shaped exactly like Entra's. It only works in Development, and switching to real Entra is a config change.

### 8. RabbitMQ

Exchange types:

- Direct sends to the queue with the exact matching key.
- Topic matches patterns like `orders.*`.
- Fanout sends a copy to every bound queue.
- Headers matches on message headers. It's rarely used.

Durable means the queue and messages survive a RabbitMQ restart.

At-least-once means a message is only removed after the worker confirms it's done. If the worker crashes halfway, the message comes back. So the worker will sometimes get the same message twice, and it must handle that safely.

Example: the worker allocates stock for order 123, then crashes before confirming. RabbitMQ sends the message again. The worker sees order 123 is already allocated and skips it. We also record each message id, so duplicates get dropped.

If a message keeps failing, we retry a few times with growing delays, then move it to an error queue to look at later, so it doesn't block everything else.

On the API side, the order and its "OrderCreated" message are saved in the same transaction (an outbox), so we never save an order and lose its message.

### 9. Frontend state and type safety

Most data in this app comes from the server, so TanStack Query handles it: caching, loading states, refetching. Filters and page numbers live in the URL so a refresh keeps them. Forms use react-hook-form with Zod. Context is only used for small global things like the theme.

Redux Toolkit is worth it for complicated client-side state. For this app it would just be extra code.

Type safety: TypeScript types are generated from the API's OpenAPI document. If the API changes a field, the web build fails instead of the app breaking in production.

Example: if someone renames `totalAmount` in the API, every place in the web app that uses it shows a compile error.

### 10. Testing and CI

- Unit tests cover the domain rules: allowed currencies, status changes, totals. They're fast and there are many.
- Integration tests run each endpoint against a real SQL Server in a container: auth, validation, idempotent status updates, the message being saved for RabbitMQ.
- Component tests check forms and lists the way a user sees them.
- A few Playwright end-to-end tests cover the main journey: create customer, create order, change its status.

Example: "PENDING to FULFILLED is not allowed" is a unit test. "PUT status with the same Idempotency-Key twice only changes it once" is an integration test.

CI runs on every pull request: build (warnings fail the build), lint, typecheck, all tests, checking the migrations against an empty database, and end-to-end tests on the Docker stack. I'd add a coverage check and security scanning (Dependabot, CodeQL) next.

### 11. Performance, scalability and observability

Write spikes: the API only does the quick part (save the order, return 201). The slow work goes to RabbitMQ, so a spike fills the queue instead of crashing the API. Rate limiting returns 429 instead of falling over.

Indexes match the queries. Example: `IX_Orders_CustomerId_Status_CreatedAt` makes "this customer's paid orders, newest first" fast.

Caching: `GET /orders/{id}` returns an ETag. If nothing changed, the API answers 304 and doesn't resend the order.

Correlation ids: every request gets an id, and it goes into every log line and into the RabbitMQ message. Example: one id shows the API creating the order and the worker allocating it in the logs.

SLOs I'd set: 99.9% of requests succeed, reads under 300 ms, and paid orders fulfilled within a minute.

## SQL questions

### 12. Pagination query

```sql
SELECT Id, Status, CreatedAt, TotalAmount
FROM Orders
WHERE CustomerId = @CustomerId
ORDER BY CreatedAt DESC, Id DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

SELECT COUNT(*) AS TotalCount
FROM Orders
WHERE CustomerId = @CustomerId;
```

The values are passed as parameters, never glued into the SQL string. `Id` is added to the sort so orders with the same time always come back in the same order. Example: page 3 with 20 per page is `@Offset = 40`, `@PageSize = 20`.

### 13. Top spenders (last 90 days)

```sql
SELECT TOP (10)
       c.Id, c.Name,
       COALESCE(SUM(o.TotalAmount), 0) AS TotalSpend
FROM Customers c
LEFT JOIN Orders o
       ON o.CustomerId = c.Id
      AND o.CreatedAt >= DATEADD(DAY, -90, SYSUTCDATETIME())
GROUP BY c.Id, c.Name
ORDER BY TotalSpend DESC;
```

The date filter goes in the `ON`, not the `WHERE`. If it were in the `WHERE`, customers with no orders would be dropped, and the question wants them shown with 0. `COALESCE` turns their empty total into 0.

In the real app we also filter by currency, because adding ZAR and BWP together makes no sense.

### 14. Indexing

```sql
CREATE INDEX IX_Orders_CustomerId_Status_CreatedAt
    ON Orders (CustomerId, Status, CreatedAt)
    INCLUDE (TotalAmount, CurrencyCode);
```

The columns we filter on exactly come first (`CustomerId`, `Status`), and the one we sort on comes last (`CreatedAt`). SQL Server can jump straight to the rows, already in order.

`INCLUDE` makes it a covering index: the list's other columns are stored in the index too, so SQL Server never has to go back to the main table. The trade-off is that every extra index slows down inserts and updates and uses more space, so only cover the queries that run a lot.

Clustered index: that's the table itself, sorted by its key. It should be small and always increasing. We use sequential GUIDs, so new orders go at the end instead of being inserted all over the table.

### 15. Execution plans and key lookups

A key lookup happens when the index finds the rows but doesn't have all the columns, so SQL Server goes back to the table for every row.

To find it, turn on the actual execution plan and look for a "Key Lookup" step. Its output list tells you which columns were missing.

Example: the query filters orders by `CustomerId` and `Status` but also selects `TotalAmount`. If the index doesn't have `TotalAmount`, every row needs a lookup. Fix it by adding `INCLUDE (TotalAmount)` to the index. The same goes for line items: an index on `OrderId` with `INCLUDE (ProductSku, Quantity, UnitPrice)`. Or just don't select columns you don't need.

### 16. Optimistic concurrency with rowversion

```csharp
public byte[] RowVersion { get; private set; } = [];

// In the EF configuration
builder.Property(o => o.RowVersion).IsRowVersion();
```

The migration adds `RowVersion rowversion` to `Orders`. SQL Server changes it on every update.

```csharp
try
{
    order.TransitionTo(OrderStatus.Paid);
    await db.SaveChangesAsync(ct);
}
catch (DbUpdateConcurrencyException)
{
    return Conflict("This order was changed by someone else. Reload and try again.");
}
```

Example: two people open the same order. Person A marks it Paid and saves. Person B, still holding the old version, tries to cancel it. B's update finds no matching row, so B gets 409 instead of silently overwriting A's change.

### 17. Deadlocks

The scenario: the worker updates an order's status. It locks the row in the table, then needs to update the index. At the same moment, someone loading the order list has the index row locked and needs the table row. Each is waiting for the other, so SQL Server kills one of them with error 1205.

The mitigation I'd use is READ COMMITTED SNAPSHOT:

```sql
ALTER DATABASE OrderManagement SET READ_COMMITTED_SNAPSHOT ON;
```

With this on, readers read the last saved version of the row and don't take locks, so a reader and a writer can't deadlock each other. Keeping transactions short and using covering indexes helps too. `NOLOCK` is not a fix, because it can read wrong data.

### 18. Running total

```sql
SELECT CustomerId, Id, CreatedAt, TotalAmount,
       SUM(TotalAmount) OVER (
           PARTITION BY CustomerId
           ORDER BY CreatedAt, Id
           ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
       ) AS RunningTotal
FROM Orders
ORDER BY CustomerId, CreatedAt, Id;
```

Each row shows how much that customer had spent up to and including that order. Example: orders of 100, 50 and 200 give running totals of 100, 150 and 350.

`ROWS` is used so two orders at the exact same time don't both get the combined total. In the app we also do this per currency.

### 19. Partitioning

Partition `Orders` by month on `CreatedAt`. New orders always land in the current month, and most queries look at recent orders.

```sql
CREATE PARTITION FUNCTION pf_OrdersByMonth (datetime2)
    AS RANGE RIGHT FOR VALUES ('2026-01-01', '2026-02-01', '2026-03-01');

CREATE PARTITION SCHEME ps_OrdersByMonth
    AS PARTITION pf_OrdersByMonth ALL TO ([PRIMARY]);
```

Archiving: instead of deleting millions of old rows (slow, and fills the log), switch the whole old month out to an archive table. It takes milliseconds because only metadata moves:

```sql
ALTER TABLE Orders SWITCH PARTITION 1 TO Orders_Archive;
```

Example: on the 1st of each month, a job adds an empty partition for next month and switches out the month that's past the retention period.

For a few million rows, good indexes are usually enough. Partitioning really pays off at hundreds of millions of rows, or when you need to archive old data regularly.

### 20. Stored procedure: transaction report

```sql
CREATE OR ALTER PROCEDURE dbo.sp_GetTransactionReport
    @StartDate  DATE,
    @EndDate    DATE,
    @Status     NVARCHAR(20)     = NULL,
    @CustomerId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Result set 1: every matching order with its customer and line items
    SELECT c.Name, c.Email, c.CountryCode,
           o.Id AS OrderId, o.Status, o.CreatedAt, o.CurrencyCode, o.TotalAmount,
           li.ProductSku, li.Quantity, li.UnitPrice
    FROM Orders o
    JOIN Customers c       ON c.Id = o.CustomerId
    JOIN OrderLineItems li ON li.OrderId = o.Id
    WHERE o.CreatedAt >= @StartDate
      AND o.CreatedAt <  DATEADD(DAY, 1, @EndDate)
      AND (@Status IS NULL OR o.Status = @Status)
      AND (@CustomerId IS NULL OR o.CustomerId = @CustomerId)
    ORDER BY o.CreatedAt, o.Id;

    -- Result set 2: summary for the same filters
    SELECT COUNT(*) AS TotalOrders,
           COALESCE(SUM(o.TotalAmount), 0) AS GrandTotalAmount
    FROM Orders o
    WHERE o.CreatedAt >= @StartDate
      AND o.CreatedAt <  DATEADD(DAY, 1, @EndDate)
      AND (@Status IS NULL OR o.Status = @Status)
      AND (@CustomerId IS NULL OR o.CustomerId = @CustomerId)
    OPTION (RECOMPILE);
END;
```

`@EndDate` is inclusive, so the filter is "before the start of the next day". That way orders at 15:00 on the end date are still included. Leaving `@Status` or `@CustomerId` as NULL means "all". The summary is counted from `Orders` only, so an order with three line items is counted once, not three times. `OPTION (RECOMPILE)` lets SQL Server pick a good plan for whichever filters were actually passed.

Example:

```sql
EXEC dbo.sp_GetTransactionReport '2026-09-01', '2026-09-30', @Status = 'Paid';
```

returns every paid order in September with its line items, then one row with the order count and grand total. If you mix currencies in one report, the grand total adds them together, so in practice I'd either filter by currency or group the summary by `CurrencyCode`.
