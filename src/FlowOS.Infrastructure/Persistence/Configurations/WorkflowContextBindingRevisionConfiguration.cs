using System.Text.Json;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.ValueObjects;
using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class WorkflowContextBindingRevisionConfiguration : IEntityTypeConfiguration<WorkflowContextBindingRevision>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void Configure(EntityTypeBuilder<WorkflowContextBindingRevision> builder)
    {
        builder.ToTable("WorkflowContextBindingRevisions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.BindingId).IsRequired();
        builder.Property(x => x.Revision).IsRequired();
        builder.Property(x => x.SourceWorkflowClassId).IsRequired();
        builder.Property(x => x.SourceWorkflowClassVersion).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Definition)
            .HasConversion(
                value => JsonSerializer.Serialize(value, JsonOptions),
                value => JsonSerializer.Deserialize<WorkflowContextBindingDefinition>(value, JsonOptions)
                    ?? new WorkflowContextBindingDefinition());
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(WorkflowContextBindingRevisionStatus.Draft);
        builder.Property(x => x.ContentHash).HasMaxLength(128).IsRequired(false);
        builder.Property(x => x.WorkflowDefinitionId).IsRequired(false);
        builder.Property(x => x.StateMachineDefinitionId).IsRequired(false);
        builder.Property(x => x.ActivatedAtUtc).IsRequired(false);
        builder.Property(x => x.SupersededAtUtc).IsRequired(false);
        builder.Property(x => x.UpdatedAtUtc).IsConcurrencyToken();

        builder.HasOne<WorkflowContextBinding>()
            .WithMany()
            .HasForeignKey(x => x.BindingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WorkflowClass>()
            .WithMany()
            .HasForeignKey(x => x.SourceWorkflowClassId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WorkflowDefinition>()
            .WithMany()
            .HasForeignKey(x => x.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StateMachineDefinition>()
            .WithMany()
            .HasForeignKey(x => x.StateMachineDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.BindingId, x.Revision }).IsUnique();
        builder.HasIndex(x => x.WorkflowDefinitionId)
            .IsUnique()
            .HasFilter("\"WorkflowDefinitionId\" IS NOT NULL");
        builder.HasIndex(x => x.StateMachineDefinitionId)
            .IsUnique()
            .HasFilter("\"StateMachineDefinitionId\" IS NOT NULL");
        builder.HasIndex(x => new { x.BindingId, x.Status });
    }
}
