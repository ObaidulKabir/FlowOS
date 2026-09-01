using FlowOS.Core.Common.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class WorkflowTimerJobConfiguration : IEntityTypeConfiguration<WorkflowTimerJob>
{
    public void Configure(EntityTypeBuilder<WorkflowTimerJob> builder)
    {
        builder.ToTable("WorkflowTimerJobs");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.WorkflowInstanceId).IsRequired();
        builder.Property(t => t.StepId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.TriggerEventType).IsRequired().HasMaxLength(200);
        builder.Property(t => t.DueTimeUtc).IsRequired();
        builder.Property(t => t.IsProcessed).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.ProcessedAt);

        builder.HasIndex(t => t.TenantId);
        builder.HasIndex(t => t.WorkflowInstanceId);
        builder.HasIndex(t => new { t.IsProcessed, t.DueTimeUtc });
    }
}
