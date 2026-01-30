using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;
using System.Collections.Generic;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("WorkflowDefinitions");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(w => w.Status)
            .HasConversion<string>();

        builder.Property(w => w.TenantId)
            .IsRequired();

        // Store Steps as JSONB
        builder.OwnsMany(w => w.Steps, step =>
        {
            step.ToJson();
            
            // Map the dictionary explicitly for JSON serialization
            step.Property(s => s.NextSteps)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                    s => JsonSerializer.Deserialize<Dictionary<string, string>>(s, (JsonSerializerOptions)null) ?? new Dictionary<string, string>()
                );
        });

        builder.HasIndex(w => w.TenantId);
    }
}
