using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderManagement.Domain.Customers;

namespace OrderManagement.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers", t =>
            t.HasCheckConstraint("CK_Customers_CountryCode", "LEN([CountryCode]) = 2"));

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(Customer.NameMaxLength).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(Customer.EmailMaxLength).IsRequired();
        builder.Property(c => c.CountryCode).HasMaxLength(2).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        // Emails are stored lower-cased by the domain, so a plain unique index is case-insensitive in practice.
        builder.HasIndex(c => c.Email).IsUnique();

        // Supports prefix search (Name LIKE 'abc%') and ORDER BY Name on the customer list.
        builder.HasIndex(c => c.Name);

        // Reports rank customers by country (e.g. every customer who can pay in ZAR). INCLUDE (Name) covers that read.
        builder.HasIndex(c => c.CountryCode).IncludeProperties(c => c.Name);
    }
}
