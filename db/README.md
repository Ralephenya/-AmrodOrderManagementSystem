# Database lifecycle

The schema is owned by EF Core code-first migrations in
[`src/OrderManagement.Infrastructure/Persistence/Migrations`](../src/OrderManagement.Infrastructure/Persistence/Migrations).
`dotnet-ef` is pinned to **9.0.20** (EF Core 9, which supports .NET 8; required by MassTransit 8.5) in the repo's tool manifest, so run `dotnet tool restore` once.

## Migration history

| Migration | Change | Kind |
|---|---|---|
| `InitialCreate` | Customers, Orders, OrderLineItems; FKs (Restrict / Cascade), check constraints, `IX_Orders_CustomerId_Status_CreatedAt` (INCLUDE TotalAmount, CurrencyCode) | create |
| `AddOrderRowVersion` | `Orders.RowVersion rowversion` for optimistic concurrency | additive |
| `AddOrderAllocatedAt` | `Orders.AllocatedAt datetime2 NULL`, set by the worker | additive, optional |
| `AddIdempotencyKeys` | `IdempotencyKeys` (PK ClientId + Key, request hash, stored outcome, `IX_IdempotencyKeys_ExpiresAt`) | additive, new table |
| `AddMessagingOutbox` | MassTransit `OutboxMessage`, `OutboxState` (transactional outbox) and `InboxState` (consumer de-duplication) | additive, new tables |
| `AddCustomerCountryCodeIndex` | `IX_Customers_CountryCode` INCLUDE (Name), for the top-spenders report (create `ONLINE = ON` on a large production table) | additive, index |

## Commands

All commands run from the repo root. Shared flags:

```bash
EF="dotnet ef --project src/OrderManagement.Infrastructure --startup-project src/OrderManagement.Infrastructure"
```

| Task | Command |
|---|---|
| Add a migration | `$EF migrations add <Name> -o Persistence/Migrations` |
| List migrations (and which are applied) | `$EF migrations list` |
| Apply to the local DB | `$EF database update` |
| Roll back to a migration | `$EF database update <TargetMigration>` (e.g. `AddOrderRowVersion`) |
| Roll back everything | `$EF database update 0` |
| Remove the last, *unapplied* migration | `$EF migrations remove` |
| Idempotent deploy script | `$EF migrations script --idempotent -o db/scripts/migrate-idempotent.sql` |
| Rollback script (from → to) | `$EF migrations script AddCustomerCountryCodeIndex AddMessagingOutbox -o db/scripts/rollback-AddCustomerCountryCodeIndex.sql` |
| Migration bundle (for containers/CD) | `$EF migrations bundle --self-contained -r linux-x64 -o efbundle` |

The design-time factory targets LocalDB by default. To point the tools somewhere else, set
`ConnectionStrings__OrdersDb`.

In local development the API applies pending migrations on startup (`Database:ApplyMigrationsOnStartup`,
which is on only in `appsettings.Development.json`). Shared environments **never** migrate from the app.
They apply the reviewed script or the bundle as a separate pipeline step, so the deploy can be approved,
rolled back and run with elevated rights that the app itself doesn't hold.

## SQL artifacts

- [`scripts/migrate-idempotent.sql`](scripts/migrate-idempotent.sql): every migration, guarded by
  `__EFMigrationsHistory` checks, so it is safe to run against a database at any version.
- [`scripts/rollback-AddCustomerCountryCodeIndex.sql`](scripts/rollback-AddCustomerCountryCodeIndex.sql): down-script for the latest migration (back to `AddMessagingOutbox`). Roll back one migration at a time, newest first.

Apply them with `sqlcmd -I` (or SSMS / Azure Data Studio, which default to it). `-I` turns on
`QUOTED_IDENTIFIER`, which SQL Server requires for the filtered indexes in the MassTransit outbox tables. Plain
`sqlcmd` defaults it off and fails with *Msg 1934*:

```bash
sqlcmd -S <server> -d OrderManagement -E -b -I -i db/scripts/migrate-idempotent.sql
```

Regenerate both after adding a migration. CI applies the idempotent script to an empty SQL Server
container to prove it compiles and runs.

## Zero-downtime migrations (expand → migrate → contract)

During a rolling deploy, old and new versions of the API run against the same database at the same time,
so every schema change must work with **both**.

1. **Expand.** Ship only additive changes: new nullable columns, new tables, new indexes (created
   `ONLINE = ON` on large tables). Old code ignores them. `AddOrderAllocatedAt` is an example.
2. **Migrate.** Deploy code that writes the new shape, and backfill existing rows in small batches
   (e.g. `UPDATE TOP (5000) … WHERE NewCol IS NULL` in a loop) to avoid long locks and log growth.
3. **Contract.** Once nothing reads the old shape, enforce constraints (`NOT NULL`, FKs) and drop or rename
   the old columns in a later release.

Renames and drops never happen in the same release as the code that stops using them. A rename is
add-new → dual-write → backfill → switch reads → drop-old.

## Rollback strategy

- **Additive migrations** (everything so far) roll back cleanly with `database update <Previous>` or the
  generated down-script. Because they're additive, the usual first move is to roll back the *app* and
  leave the schema in place, since old code ignores new columns.
- **Destructive migrations** (drop or narrow a column) lose data on `Down`. Before applying one:
  take a backup (`BACKUP DATABASE … WITH COPY_ONLY`, or a point-in-time restore point in Azure SQL), and
  keep the dropped data in an archive table until the release is proven. Roll back by restoring, not by `Down`.
- Every migration is reviewed as SQL (the script) before it reaches a shared environment.
