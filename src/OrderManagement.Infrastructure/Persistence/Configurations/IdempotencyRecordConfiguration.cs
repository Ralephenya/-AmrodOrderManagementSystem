using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderManagement.Infrastructure.Idempotency;

namespace OrderManagement.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyKeys");

        // The composite primary key is the concurrency guard: two in-flight requests with the same key can't both insert.
        builder.HasKey(r => new { r.ClientId, r.Key });

        builder.Property(r => r.ClientId).HasMaxLength(IdempotencyRecord.ClientIdMaxLength).IsUnicode(false);
        builder.Property(r => r.Key).HasMaxLength(IdempotencyRecord.KeyMaxLength).IsUnicode(false);
        builder.Property(r => r.RequestHash).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        builder.Property(r => r.Payload).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.ExpiresAt).IsRequired();

        // For the expiry sweep (DELETE … WHERE ExpiresAt < now).
        builder.HasIndex(r => r.ExpiresAt);
    }
}
