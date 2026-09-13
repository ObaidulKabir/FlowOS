using FlowOS.Core.Common.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class CapabilityBindingRecordConfiguration : IEntityTypeConfiguration<CapabilityBindingRecord>
{
    public void Configure(EntityTypeBuilder<CapabilityBindingRecord> builder)
    {
        builder.ToTable("CapabilityBindings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.CapabilityName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Transport).IsRequired().HasMaxLength(30);
        builder.Property(x => x.EndpointUrl).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.AuthRef).HasMaxLength(300);
        builder.Property(x => x.RequestSchemaVersion).HasMaxLength(50);
        builder.Property(x => x.ResponseSchemaVersion).HasMaxLength(50);
        builder.Property(x => x.RetryPolicy).IsRequired().HasMaxLength(60);
        builder.Property(x => x.TimeoutMs).IsRequired();
        builder.Property(x => x.IsEnabled).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.CapabilityName }).IsUnique();
        builder.HasIndex(x => x.IsEnabled);
    }
}
