using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

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
            step.Ignore(s => s.NextSteps);
        });

        builder.HasIndex(w => w.TenantId);
    }
}
