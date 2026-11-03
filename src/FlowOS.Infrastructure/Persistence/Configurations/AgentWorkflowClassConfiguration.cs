using System.Text.Json;
using FlowOS.Domain;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class AgentWorkflowClassConfiguration : IEntityTypeConfiguration<AgentWorkflowClass>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void Configure(EntityTypeBuilder<AgentWorkflowClass> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Version)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.FlowOsVersion)
            .IsRequired()
            .HasMaxLength(32)
            .HasDefaultValue(FlowOsRelease.PreStampVersion);

        builder.Property(e => e.ChangeLog)
            .HasMaxLength(2000);

        builder.Property(e => e.DeprecationReason)
            .HasMaxLength(2000);

        // Serialise the entire blueprint as jsonb — mirrors WorkflowClassConfiguration
        builder.Property(e => e.Definition)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<AgentWorkflowBlueprint>(v, JsonOptions)
                     ?? new AgentWorkflowBlueprint()
            )
            .HasColumnType("jsonb");

        // Fast tenant-scoped lookups (matches WorkflowClass index pattern)
        builder.HasIndex(e => e.TenantId);

        // Lineage — non-unique; many versions may share the same predecessor
        builder.HasIndex(e => e.PreviousVersionId);
    }
}
