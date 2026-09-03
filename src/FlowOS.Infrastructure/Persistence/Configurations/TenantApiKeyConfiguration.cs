using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class TenantApiKeyConfiguration : IEntityTypeConfiguration<TenantApiKey>
{
    public void Configure(EntityTypeBuilder<TenantApiKey> builder)
    {
        builder.ToTable("TenantApiKeys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(k => k.KeyPrefix)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(k => k.KeyHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(k => k.MaskedKey)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(k => k.TenantId);
        builder.HasIndex(k => k.KeyHash).IsUnique();
    }
}
