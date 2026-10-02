using FlowOS.Domain.Entities.ExternalAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public sealed class ExternalAgentPlanRecordConfiguration : IEntityTypeConfiguration<ExternalAgentPlanRecord>
{
    public void Configure(EntityTypeBuilder<ExternalAgentPlanRecord> builder)
    {
        builder.ToTable("ExternalAgentPlanRecords");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ChangeId).IsRequired();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.AgentProfileId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.PlanStepsJson).IsRequired();
        builder.Property(x => x.PlanVersion).IsRequired().HasDefaultValue(1);

        builder.HasIndex(x => x.ChangeId)
            .HasDatabaseName("IX_ExternalAgentPlanRecords_ChangeId");
        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("IX_ExternalAgentPlanRecords_TenantId");
    }
}
