using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class WorkflowActionExecutionLogConfiguration : IEntityTypeConfiguration<WorkflowActionExecutionLog>
{
    public void Configure(EntityTypeBuilder<WorkflowActionExecutionLog> builder)
    {
        builder.ToTable("WorkflowActionExecutionLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId).IsRequired();
        builder.Property(x => x.StepId).IsRequired().HasMaxLength(150);
        builder.Property(x => x.TriggerPhase).IsRequired().HasMaxLength(50);
        builder.Property(x => x.ActionType).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Target).HasMaxLength(1000);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(50);
        builder.Property(x => x.ExecutedAtUtc).IsRequired();
        builder.Property(x => x.DurationMs).IsRequired();
        builder.Property(x => x.HttpStatusCode);
        builder.Property(x => x.RequestPayloadSnippet).HasMaxLength(4000);
        builder.Property(x => x.ResponseSnippet).HasMaxLength(4000);
        builder.Property(x => x.ErrorMessage);
        builder.Property(x => x.AttemptNumber).IsRequired();
        builder.Property(x => x.OutboxMessageId);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.WorkflowInstanceId);
        builder.HasIndex(x => new { x.TenantId, x.WorkflowInstanceId, x.ExecutedAtUtc });
    }
}
