using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Orders;

namespace OrderManagement.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", t =>
        {
            t.HasCheckConstraint("CK_Orders_TotalAmount", "[TotalAmount] >= 0");
            t.HasCheckConstraint(
                "CK_Orders_Status",
                $"[Status] IN ({string.Join(", ", Enum.GetNames<OrderStatus>().Select(n => $"'{n}'"))})");
        });

        builder.HasKey(o => o.Id);

        // Stored as text ('Pending', 'Paid', …) so the data reads clearly in SQL and reports.
        // Trade-off: renaming an enum member becomes a data migration.
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false).IsRequired();

        builder.Property(o => o.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(o => o.TotalAmount).HasPrecision(Money.Precision, Money.Scale);
        builder.Property(o => o.CreatedAt).IsRequired();
        builder.Property(o => o.AllocatedAt);
        builder.Property(o => o.RowVersion).IsRowVersion();

        // Ignored: computed from Status, not stored.
        builder.Ignore(o => o.AllowedNextStatuses);

        // Restrict: a customer with orders can't be deleted out from under them.
        builder.HasOne(o => o.Customer)
            .WithMany()
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cascade: line items have no life outside their order.
        builder.HasMany(o => o.LineItems)
            .WithOne()
            .HasForeignKey(li => li.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.LineItems).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The brief's listing index. INCLUDE makes it covering for the list query (no key lookups).
        builder.HasIndex(o => new { o.CustomerId, o.Status, o.CreatedAt })
            .HasDatabaseName("IX_Orders_CustomerId_Status_CreatedAt")
            .IncludeProperties(o => new { o.TotalAmount, o.CurrencyCode });

        // Listing across all customers (admin view): filter by status, newest first.
        builder.HasIndex(o => new { o.Status, o.CreatedAt })
            .HasDatabaseName("IX_Orders_Status_CreatedAt")
            .IncludeProperties(o => new { o.CustomerId, o.TotalAmount, o.CurrencyCode });
    }
}
