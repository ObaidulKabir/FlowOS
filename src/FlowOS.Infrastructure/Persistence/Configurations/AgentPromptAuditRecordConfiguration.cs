using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public sealed class AgentPromptAuditRecordConfiguration : IEntityTypeConfiguration<AgentPromptAuditRecord>
{
    public void Configure(EntityTypeBuilder<AgentPromptAuditRecord> builder)
    {
        builder.ToTable("AgentPromptAuditRecords");
        builder.HasKey(x => x.AuditId);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId);
        builder.Property(x => x.StepId).HasMaxLength(200);
        builder.Property(x => x.ProviderAlias).HasMaxLength(200);
        builder.Property(x => x.ProviderName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Model).IsRequired().HasMaxLength(200);
        builder.Property(x => x.RecordedAtUtc).IsRequired();
        builder.Property(x => x.SystemPrompt);
        builder.Property(x => x.UserPrompt);
        builder.Property(x => x.RawRequestPayload).IsRequired();
        builder.Property(x => x.RawResponsePayload);
        builder.Property(x => x.FailureCode).HasMaxLength(100);
        builder.Property(x => x.HttpStatusCode);
        builder.Property(x => x.Iteration).IsRequired();
        builder.Property(x => x.InputTokens);
        builder.Property(x => x.OutputTokens);
        builder.Property(x => x.DurationMs).IsRequired();
        builder.Property(x => x.ExecutionId);
        builder.Property(x => x.CorrelationId);

        builder.HasIndex(x => new { x.TenantId, x.RecordedAtUtc })
            .HasDatabaseName("IX_AgentPromptAuditRecords_Tenant_RecordedAt")
            .IsDescending(false, true);
        builder.HasIndex(x => x.WorkflowInstanceId)
            .HasDatabaseName("IX_AgentPromptAuditRecords_WorkflowInstanceId");
        builder.HasIndex(x => x.ExecutionId)
            .HasDatabaseName("IX_AgentPromptAuditRecords_ExecutionId");
    }
}
