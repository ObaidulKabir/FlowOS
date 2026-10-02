using FlowOS.Domain.Entities.ExternalAI;
using FlowOS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public sealed class ExternalAgentPlanStepRecordConfiguration : IEntityTypeConfiguration<ExternalAgentPlanStepRecord>
{
    public void Configure(EntityTypeBuilder<ExternalAgentPlanStepRecord> builder)
    {
        builder.ToTable("ExternalAgentPlanStepRecords");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.PlanId).IsRequired();
        builder.Property(x => x.StepId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.StepIndex).IsRequired();
        builder.Property(x => x.ToolName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ToolArgsJson).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.DependsOnJson).IsRequired();
        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(ExternalAgentStepStatus.Pending);
        builder.Property(x => x.StartedAtUtc);
        builder.Property(x => x.FinishedAtUtc);
        builder.Property(x => x.ResultSnapshotJson).HasMaxLength(65535);
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);

        builder.HasIndex(x => new { x.PlanId, x.StepIndex })
            .HasDatabaseName("IX_ExternalAgentPlanStepRecords_PlanId_StepIndex");
    }
}
