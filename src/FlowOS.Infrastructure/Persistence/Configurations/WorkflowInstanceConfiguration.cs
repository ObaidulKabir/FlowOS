using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("WorkflowInstances");

        builder.HasKey(w => w.Id);
        builder.HasAlternateKey(w => new { w.Id, w.TenantId });

        builder.Property(w => w.WorkflowDefinitionId)
            .IsRequired();

        builder.Property(w => w.ParentWorkflowInstanceId)
            .IsRequired(false);

        builder.Property(w => w.ParentStepId)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(w => w.CurrentStepId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(w => w.CurrentState)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(w => w.Status)
            .HasConversion<string>();

        builder.Property(w => w.TenantId)
            .IsRequired();

        builder.Property(w => w.CorrelationId)
            .IsRequired(false);

        builder.Property(w => w.ActiveStepIds)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? new System.Collections.Generic.List<string>() : System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new System.Collections.Generic.List<string>()
            );

        builder.Property(w => w.CompletedParallelStepIds)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? new System.Collections.Generic.List<string>() : System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new System.Collections.Generic.List<string>()
            );

        builder.Property(w => w.PathTravelCounts)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.Ordinal)
                    : System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, int>>(v, (System.Text.Json.JsonSerializerOptions?)null)
                      ?? new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.Ordinal)
            );

        builder.HasIndex(w => w.TenantId);
        builder.HasIndex(w => w.CorrelationId);
        builder.HasIndex(w => w.WorkflowDefinitionId);
        builder.HasIndex(w => new { w.TenantId, w.ParentWorkflowInstanceId, w.ParentStepId });
    }
}
