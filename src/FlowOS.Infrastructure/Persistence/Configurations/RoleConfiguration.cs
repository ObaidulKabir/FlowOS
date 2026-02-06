using System.Text.Json;
using System.Collections.Generic;
using System.Linq; // Add this
using FlowOS.Security.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking; // Add this

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.TenantId)
            .IsRequired();

        // Unique index on (TenantId, Name)
        builder.HasIndex(r => new { r.TenantId, r.Name })
            .IsUnique();

        // Store Permissions as JSON with Value Comparer
        var comparer = new ValueComparer<HashSet<string>>(
            (c1, c2) => c1.SequenceEqual(c2),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => new HashSet<string>(c));

        builder.Property(r => r.Permissions)
            .HasConversion(
                v => JsonSerializer.Serialize(v.ToList(), (JsonSerializerOptions)null),
                v => new HashSet<string>(JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions)null) ?? new List<string>())
            )
            .Metadata.SetValueComparer(comparer);
    }
}
