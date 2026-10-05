---
description: Add EF Core soft delete + audit via a single SaveChangesInterceptor and reflection-applied query filters
argument-hint: [entity name, defaults to every ISoftDeletable entity]
allowed-tools: Bash, Read, Write, Edit, Glob, Grep
---

# Add soft delete + audit via one EF Core interceptor

Target: **$ARGUMENTS** — if empty, apply to every entity that should be soft-deletable.

This must be done with **an interceptor plus automatically-applied global query filters**,
never with manual `IsDeleted` checks scattered through queries. If you find yourself
writing `.Where(x => !x.IsDeleted)` in a service, you are doing it wrong.

## House style: ONE interceptor, not two

The reference solution uses a single `AuditAndSoftDeleteInterceptor` handling both
concerns in one `ApplyRules` pass. Match that — do not split it into two classes.

## Steps

1. **Marker interfaces** in `Interfaces/`:
   ```csharp
   public interface ISoftDeletable { bool IsDeleted { get; set; } }
   public interface IAuditable { DateTime CreatedAt { get; set; } DateTime? ModifiedAt { get; set; } }
   ```
   Implement them on the target entities in `Models/` (`public bool IsDeleted { get; set; } = false;`).

2. **`Data/Interceptors/AuditAndSoftDeleteInterceptor.cs`**, sealed, inheriting the
   **concrete `SaveChangesInterceptor`** (not the raw interface):
   ```csharp
   public sealed class AuditAndSoftDeleteInterceptor : SaveChangesInterceptor
   {
       public const string SoftDeleteFilterName = "SoftDelete";

       public override InterceptionResult<int> SavingChanges(
           DbContextEventData eventData, InterceptionResult<int> result)
       {
           if (eventData.Context is not null) ApplyRules(eventData.Context);
           return base.SavingChanges(eventData, result);
       }

       public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
           DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
       {
           if (eventData.Context is not null) ApplyRules(eventData.Context);
           return base.SavingChangesAsync(eventData, result, ct);
       }

       private static void ApplyRules(DbContext context)
       {
           foreach (var entry in context.ChangeTracker.Entries())
           {
               if (entry.Entity is ISoftDeletable sd && entry.State == EntityState.Deleted)
               {
                   entry.State = EntityState.Modified;
                   sd.IsDeleted = true;
               }

               if (entry.Entity is IAuditable a)
               {
                   if (entry.State == EntityState.Added) a.CreatedAt = DateTime.UtcNow;
                   else if (entry.State == EntityState.Modified) a.ModifiedAt = DateTime.UtcNow;
               }
           }
       }
   }
   ```
   > Override **both** `SavingChanges` and `SavingChangesAsync`. A sync-only override is a
   > silent bug: `SaveChangesAsync` bypasses it and issues a real `DELETE`.
   >
   > Order matters — flip `Deleted` → `Modified` **before** the audit block, so a
   > soft delete also stamps `ModifiedAt`.

3. **Query filters applied by reflection** in `AppDbContext.OnModelCreating` — do not
   hand-write one `HasQueryFilter` per entity:
   ```csharp
   private static void ApplySoftDeleteFilters(ModelBuilder modelBuilder)
   {
       foreach (var entityType in modelBuilder.Model.GetEntityTypes())
       {
           if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType)) continue;

           var param = Expression.Parameter(entityType.ClrType, "e");
           var prop = Expression.Property(param, nameof(ISoftDeletable.IsDeleted));
           var lambda = Expression.Lambda(Expression.Not(prop), param);
           modelBuilder.Entity(entityType.ClrType)
               .HasQueryFilter(AuditAndSoftDeleteInterceptor.SoftDeleteFilterName, lambda);
       }
   }
   ```
   Call it from `OnModelCreating` after `ApplyConfigurationsFromAssembly`. The **named**
   filter overload lets a specific filter be disabled by name later.

4. **Escape hatch** — add a method using `.IgnoreQueryFilters()` to the service interface
   in `Services/Interfaces/` and implement it, so the hatch is reachable and testable.
   Surface it on an admin action in the `Controllers/v1/` controller, returning the usual
   `ApiResponse<T>` envelope.

5. **Registration** — in `Extensions/InfrastructureExtensions.cs`, as a singleton attached
   to the context options:
   ```csharp
   services.AddSingleton<AuditAndSoftDeleteInterceptor>();
   services.AddDbContext<AppDbContext>((sp, options) =>
       options.UseSqlite(config.GetConnectionString("DefaultConnection"))
              .AddInterceptors(sp.GetRequiredService<AuditAndSoftDeleteInterceptor>()));
   ```
   Any test factory that re-registers the DbContext must re-attach the interceptor too, or
   soft delete silently stops working under test.

## Known bug in the house style — fix every call site, don't just note it

**`DbSet<T>.FindAsync(id)` bypasses global query filters — this is documented EF Core
behavior, not a mistake specific to any one project.** The reference solution this house
style is copied from uses `FindAsync` for every by-id lookup in every service
(`GetByIdAsync`, `UpdateAsync`, `DeleteAsync`), which means a soft-deleted row is still
readable, still updatable, and can be "deleted" again through those exact methods —
silently defeating the interceptor + filter you just built. Confirmed by mutation test:
reverting one call site from `FirstOrDefaultAsync` back to `FindAsync` makes a soft-delete
regression test fail immediately; reverting the fix makes it pass again.

**Fix every by-id lookup on a soft-deletable entity** to use `FirstOrDefaultAsync`
instead, which *does* respect `HasQueryFilter`:

```csharp
// Wrong — silently returns a soft-deleted row
var entity = await db.Categories.FindAsync([id], ct);

// Right — respects the global query filter
var entity = await db.Categories.FirstOrDefaultAsync(c => c.CategoryID == id, ct);
```

Grep the codebase for `FindAsync` on any `ISoftDeletable` entity's `DbSet` and replace
every occurrence — this applies retroactively to existing services, not just new code
written after this command runs.

6. **Migration** — `dotnet ef migrations add AddSoftDeleteAndAuditColumns`, then build.

## Self-evaluation — mandatory

1. `dotnet build` succeeds.
2. Verify for real. Run the app (`dotnet run --no-launch-profile --urls http://localhost:<port>` —
   `launchSettings.json` overrides `ASPNETCORE_URLS`) and execute:
   - `POST` create → note the id
   - `DELETE` it → expect the success envelope
   - `GET` by id and the list → the entity is **gone**
   - the `IgnoreQueryFilters()` admin action → the entity is **present**, `isDeleted: true`
3. Confirm the row physically survives (query the `.db` directly, or via the admin action)
   — proving no real `DELETE` was issued.
4. Confirm the async path specifically: the delete you tested went through
   `SaveChangesAsync`, which is what the controller actually calls.

## Output

Print the curl sequence you ran with its real responses, then a 2–3 sentence tradeoff
explanation suitable for saying out loud in an interview:

> Soft delete keeps the row, so deletes are recoverable and the audit trail stays intact —
> but the table only ever grows, unique indexes still collide with "deleted" rows unless
> they're made filtered, and any query that bypasses the global filter silently leaks
> deleted data. Doing it in an interceptor plus a model-wide filter means the rest of the
> codebase never has to remember any of it.
