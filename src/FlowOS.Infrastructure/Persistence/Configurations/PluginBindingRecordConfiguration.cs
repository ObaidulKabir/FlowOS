using FlowOS.Core.Common.Models;
using FlowOS.Domain;
using FlowOS.Domain.Entities;
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
        builder.Property(x => x.FlowOsVersion).IsRequired().HasMaxLength(32).HasDefaultValue(FlowOsRelease.PreStampVersion);

        // An alias is unique among the tenant-shared bindings, and separately within each application, so
        // an application can define its own prompt or agent profile with the same alias as a shared one.
        builder.HasIndex(x => new { x.TenantId, x.BindingType, x.SourceName })
            .IsUnique()
            .HasFilter("\"ApplicationId\" IS NULL");
        builder.HasIndex(x => new { x.TenantId, x.ApplicationId, x.BindingType, x.SourceName })
            .IsUnique()
            .HasFilter("\"ApplicationId\" IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.BindingType, x.IsEnabled });

        // An application with prompts or agent profiles cannot be deleted; delete or move them first.
        builder.HasOne<TenantApplication>()
            .WithMany()
            .HasForeignKey(x => x.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
