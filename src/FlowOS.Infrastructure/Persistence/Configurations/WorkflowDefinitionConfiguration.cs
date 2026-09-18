using FlowOS.Domain.Entities;
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
        builder.Property(w => w.SourceWorkflowClassId).IsRequired(false);
        builder.Property(w => w.ContextBindingRevisionId).IsRequired(false);
        builder.Property(w => w.StateMachineDefinitionId).IsRequired(false);
        builder.HasOne<WorkflowClass>()
            .WithMany()
            .HasForeignKey(w => w.SourceWorkflowClassId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StateMachineDefinition>()
            .WithMany()
            .HasForeignKey(w => w.StateMachineDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

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
            step.OwnsOne(s => s.AutoCommit, autoCommit =>
            {
                autoCommit.Property(x => x.AllowedEvents)
                    .HasConversion(
                        d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                        s => JsonSerializer.Deserialize<List<string>>(s, (JsonSerializerOptions)null) ?? new List<string>()
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

            step.Property(s => s.PathLimits)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                    s => DeserializePathLimits(s)
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

            step.Property(s => s.AgentTools)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                    s => JsonSerializer.Deserialize<List<string>>(s, (JsonSerializerOptions)null) ?? new List<string>()
                );

            step.Property(s => s.RequiredCapabilities)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                    s => JsonSerializer.Deserialize<List<string>>(s, (JsonSerializerOptions)null) ?? new List<string>()
                );

            step.Property(s => s.EventRequiredCapabilities)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions)null),
                    s => DeserializeEventCapabilities(s)
                );
        });

        // Current Index (Non-Unique)
        builder.HasIndex(w => w.TenantId);
        builder.HasIndex(w => w.SourceWorkflowClassId);
        builder.HasIndex(w => w.ContextBindingRevisionId);
        builder.HasIndex(w => w.StateMachineDefinitionId);

        // Unique Constraint for Versioning
        builder.HasIndex(w => new { w.TenantId, w.Name, w.Version })
            .IsUnique();
    }

    private static Dictionary<string, List<string>> DeserializeEventCapabilities(string? json)
    {
        var parsed = string.IsNullOrEmpty(json)
            ? new Dictionary<string, List<string>>()
            : JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, (JsonSerializerOptions)null)
              ?? new Dictionary<string, List<string>>();
        return new Dictionary<string, List<string>>(parsed, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, PathTravelLimit> DeserializePathLimits(string? json)
    {
        var parsed = string.IsNullOrEmpty(json)
            ? new Dictionary<string, PathTravelLimit>()
            : JsonSerializer.Deserialize<Dictionary<string, PathTravelLimit>>(json, (JsonSerializerOptions)null)
              ?? new Dictionary<string, PathTravelLimit>();
        return new Dictionary<string, PathTravelLimit>(parsed, StringComparer.OrdinalIgnoreCase);
    }
}
