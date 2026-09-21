using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public sealed class AgentExecutionRecordConfiguration : IEntityTypeConfiguration<AgentExecutionRecord>
{
    public void Configure(EntityTypeBuilder<AgentExecutionRecord> builder)
    {
        builder.ToTable("AgentExecutionRecords");
        builder.HasKey(x => x.ExecutionId);

        builder.Property(x => x.JobId);
        builder.Property(x => x.Claimant).HasMaxLength(200);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId).IsRequired();
        builder.Property(x => x.StepId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Actor).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.DecisionOutcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ProviderAlias).HasMaxLength(200);
        builder.Property(x => x.ProviderName).HasMaxLength(200);
        builder.Property(x => x.Model).HasMaxLength(200);
        builder.Property(x => x.PromptAlias).HasMaxLength(200);
        builder.Property(x => x.RuntimeIdentifier).HasMaxLength(200);
        builder.Property(x => x.RuntimeVersion).HasMaxLength(100);
        builder.Property(x => x.WorkflowDefinitionId);
        builder.Property(x => x.WorkflowDefinitionVersion);
        builder.Property(x => x.ContextBindingId);
        builder.Property(x => x.ContextBindingRevisionId);
        builder.Property(x => x.ContextVersion);
        builder.Property(x => x.StartedAtUtc).IsRequired();
        builder.Property(x => x.EndedAtUtc);
        builder.Property(x => x.DurationMs);
        builder.Property(x => x.Success);
        builder.Property(x => x.FailureCode).HasMaxLength(100);
        builder.Property(x => x.SanitizedFailure).HasMaxLength(2000);
        builder.Property(x => x.HttpStatusCode);
        builder.Property(x => x.InputTokens);
        builder.Property(x => x.OutputTokens);
        builder.Property(x => x.SuggestedEvent).HasMaxLength(200);
        builder.Property(x => x.Confidence);
        builder.Property(x => x.WasCommitted).IsRequired();
        builder.Property(x => x.WasParked).IsRequired();
        builder.Property(x => x.ParkReason).HasMaxLength(1000);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(300);
        builder.Property(x => x.CorrelationId);
        builder.Property(x => x.ObservedOutcome).HasMaxLength(500);
        builder.Property(x => x.OutcomeMatchedSuggestion);
        builder.Property(x => x.OutcomeEvaluatedAtUtc);
        builder.Property(x => x.WasOverridden).IsRequired();
        builder.Property(x => x.OverrideActor).HasMaxLength(200);
        builder.Property(x => x.OverrideEvent).HasMaxLength(200);
        builder.Property(x => x.OverrideReason).HasMaxLength(1000);
        builder.Property(x => x.OverriddenAtUtc);

        builder.HasIndex(x => x.JobId);
        builder.HasIndex(x => new { x.TenantId, x.WorkflowInstanceId, x.StartedAtUtc })
            .HasDatabaseName("IX_AgentExecutionRecords_Tenant_Instance_Time");
        builder.HasIndex(x => new { x.TenantId, x.StartedAtUtc })
            .HasDatabaseName("IX_AgentExecutionRecords_Tenant_Time");
    }
}
