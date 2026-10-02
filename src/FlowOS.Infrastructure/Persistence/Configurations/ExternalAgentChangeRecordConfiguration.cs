using FlowOS.Domain.Entities.ExternalAI;
using FlowOS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public sealed class ExternalAgentChangeRecordConfiguration : IEntityTypeConfiguration<ExternalAgentChangeRecord>
{
    public void Configure(EntityTypeBuilder<ExternalAgentChangeRecord> builder)
    {
        builder.ToTable("ExternalAgentChangeRecords");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.SourceOutboxMessageId);
        builder.Property(x => x.EventType).IsRequired().HasMaxLength(200);
        builder.Property(x => x.PayloadJson).IsRequired();
        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(ExternalAgentChangeStatus.Pending);
        builder.Property(x => x.LeasedByAgent).HasMaxLength(200);
        builder.Property(x => x.LeasedUntilUtc);
        builder.Property(x => x.AttemptCount).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.MaxAttempts).IsRequired().HasDefaultValue(5);
        builder.Property(x => x.NextRetryUtc);
        builder.Property(x => x.LastError).HasMaxLength(4000);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.ProcessedAtUtc);

        builder.HasIndex(x => new { x.TenantId, x.CreatedAtUtc, x.Id })
            .HasDatabaseName("IX_ExternalAgentChangeRecords_Tenant_CreatedAt_Id");
        builder.HasIndex(x => new { x.Status, x.NextRetryUtc })
            .HasDatabaseName("IX_ExternalAgentChangeRecords_Status_NextRetryUtc");
        builder.HasIndex(x => x.LeasedUntilUtc)
            .HasDatabaseName("IX_ExternalAgentChangeRecords_LeasedUntilUtc");
    }
}
