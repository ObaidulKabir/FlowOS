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

        builder.Property(k => k.ApplicationName)
            .IsRequired()
            .HasMaxLength(150)
            .HasDefaultValue("Default Application");

        builder.Property(k => k.Environment)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("Production");

        var listComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<System.Collections.Generic.List<string>>(
            (c1, c2) => c1 == null && c2 == null ? true : (c1 == null || c2 == null ? false : System.Linq.Enumerable.SequenceEqual(c1, c2)),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => new System.Collections.Generic.List<string>(c));

        builder.Property(k => k.Scopes)
            .IsRequired()
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => string.IsNullOrWhiteSpace(v) 
                    ? new System.Collections.Generic.List<string>() 
                    : System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new System.Collections.Generic.List<string>())
            .Metadata.SetValueComparer(listComparer);

        builder.Property(k => k.ExpiresAt)
            .IsRequired(false);

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
        builder.HasIndex(k => new { k.TenantId, k.ApplicationName });
        builder.HasIndex(k => k.KeyHash).IsUnique();
    }
}
