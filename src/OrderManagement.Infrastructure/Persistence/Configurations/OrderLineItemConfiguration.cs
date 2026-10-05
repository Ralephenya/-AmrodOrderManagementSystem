using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Orders;

namespace OrderManagement.Infrastructure.Persistence.Configurations;

internal sealed class OrderLineItemConfiguration : IEntityTypeConfiguration<OrderLineItem>
{
    public void Configure(EntityTypeBuilder<OrderLineItem> builder)
    {
        builder.ToTable("OrderLineItems", t =>
        {
            t.HasCheckConstraint("CK_OrderLineItems_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_OrderLineItems_UnitPrice", "[UnitPrice] >= 0");
        });

        builder.HasKey(li => li.Id);

        builder.Property(li => li.ProductSku)
            .HasMaxLength(OrderLineItem.ProductSkuMaxLength)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(li => li.UnitPrice).HasPrecision(Money.Precision, Money.Scale);

        builder.Ignore(li => li.LineTotal);

        // Backstop for the domain's duplicate-SKU rule. It also serves as the OrderId index for the FK join.
        builder.HasIndex(li => new { li.OrderId, li.ProductSku }).IsUnique();
    }
}
