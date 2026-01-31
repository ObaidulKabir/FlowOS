using FlowOS.Security.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class PolicyConfiguration : IEntityTypeConfiguration<Policy>
{
    public void Configure(EntityTypeBuilder<Policy> builder)
    {
        builder.ToTable("Policies");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.TenantId)
            .IsRequired();

        builder.Property(p => p.ConditionJson)
            .IsRequired()
            .HasColumnType("jsonb"); // Or "text" if DB doesn't support jsonb

        // Unique Policy Name per Tenant
        builder.HasIndex(p => new { p.TenantId, p.Name })
            .IsUnique();
    }
}
