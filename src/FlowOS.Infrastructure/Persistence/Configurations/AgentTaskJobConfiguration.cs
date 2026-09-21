using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public sealed class AgentTaskJobConfiguration : IEntityTypeConfiguration<AgentTaskJob>
{
    public void Configure(EntityTypeBuilder<AgentTaskJob> builder)
    {
        builder.ToTable("AgentTaskJobs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId).IsRequired();
        builder.Property(x => x.StepId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.RequestedAgentId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Objective).HasMaxLength(2000);
        builder.Property(x => x.AllowAutoCommit).IsRequired();
        builder.Property(x => x.RequireAgentActor).IsRequired();
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.ActiveKey).HasMaxLength(450);
        builder.Property(x => x.Attempts).IsRequired();
        builder.Property(x => x.MaxAttempts).IsRequired();
        builder.Property(x => x.RequestedAtUtc).IsRequired();
        builder.Property(x => x.DueAtUtc).IsRequired();
        builder.Property(x => x.ClaimedAtUtc);
        builder.Property(x => x.ClaimExpiresAtUtc);
        builder.Property(x => x.CompletedAtUtc);
        builder.Property(x => x.Claimant).HasMaxLength(200);
        builder.Property(x => x.LastError).HasMaxLength(2000);

        builder.HasIndex(x => x.ActiveKey)
            .IsUnique()
            .HasFilter("\"ActiveKey\" IS NOT NULL");
        builder.HasIndex(x => new { x.Status, x.DueAtUtc, x.RequestedAtUtc })
            .HasDatabaseName("IX_AgentTaskJobs_Polling");
        builder.HasIndex(x => new { x.Status, x.ClaimExpiresAtUtc })
            .HasDatabaseName("IX_AgentTaskJobs_ExpiredClaims");
        builder.HasIndex(x => new { x.TenantId, x.WorkflowInstanceId, x.StepId });
    }
}
