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
            step.OwnsOne(s => s.Sla, sla =>
            {
                sla.Property(x => x.Reminders)
                    .HasConversion(
                        d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                        s => JsonSerializer.Deserialize<List<StepReminderDefinition>>(s, (JsonSerializerOptions)null) ?? new List<StepReminderDefinition>()
                    );
            });
            step.OwnsOne(s => s.SubWorkflow, sub =>
            {
                sub.Property(s => s.InputMapping)
                    .HasConversion(
                        d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                        s => JsonSerializer.Deserialize<Dictionary<string, string>>(s, (JsonSerializerOptions)null) ?? new Dictionary<string, string>()
                    );

                sub.Property(s => s.OutputMapping)
                    .HasConversion(
                        d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                        s => JsonSerializer.Deserialize<Dictionary<string, string>>(s, (JsonSerializerOptions)null) ?? new Dictionary<string, string>()
                    );
            });
            
            // Map the dictionary explicitly for JSON serialization
            step.Property(s => s.NextSteps)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                    s => JsonSerializer.Deserialize<Dictionary<string, string>>(s, (JsonSerializerOptions)null) ?? new Dictionary<string, string>()
                );

            // Map Conditions dictionary
            step.Property(s => s.Conditions)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                    s => JsonSerializer.Deserialize<Dictionary<string, string>>(s, (JsonSerializerOptions)null) ?? new Dictionary<string, string>()
                );

            // Map OnEntry and OnExit action lists
            step.Property(s => s.OnEntry)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                    s => JsonSerializer.Deserialize<List<StepActionDefinition>>(s, (JsonSerializerOptions)null) ?? new List<StepActionDefinition>()
                );

            step.Property(s => s.OnExit)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                    s => JsonSerializer.Deserialize<List<StepActionDefinition>>(s, (JsonSerializerOptions)null) ?? new List<StepActionDefinition>()
                );

            step.Property(s => s.OnFailure)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                    s => JsonSerializer.Deserialize<List<StepActionDefinition>>(s, (JsonSerializerOptions)null) ?? new List<StepActionDefinition>()
                );

            step.Property(s => s.Branches)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                    s => JsonSerializer.Deserialize<List<string>>(s, (JsonSerializerOptions)null) ?? new List<string>()
                );

            step.Property(s => s.InboundSteps)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                    s => JsonSerializer.Deserialize<List<string>>(s, (JsonSerializerOptions)null) ?? new List<string>()
                );
        });

        // Current Index (Non-Unique)
        builder.HasIndex(w => w.TenantId);

        // Unique Constraint for Versioning
        builder.HasIndex(w => new { w.TenantId, w.Name, w.Version })
            .IsUnique();
    }
}
