using FlowOS.Core.Common.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class PluginBindingRecordConfiguration : IEntityTypeConfiguration<PluginBindingRecord>
{
    public void Configure(EntityTypeBuilder<PluginBindingRecord> builder)
    {
        builder.ToTable("PluginBindings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.BindingType).IsRequired().HasMaxLength(20);
        builder.Property(x => x.SourceName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ProviderName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.IsEnabled).IsRequired();
        builder.Property(x => x.ConfigurationJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.BindingType, x.SourceName }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.BindingType, x.IsEnabled });
    }
}
