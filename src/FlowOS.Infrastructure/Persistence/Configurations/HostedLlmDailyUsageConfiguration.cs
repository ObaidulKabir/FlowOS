using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public sealed class HostedLlmDailyUsageConfiguration : IEntityTypeConfiguration<HostedLlmDailyUsage>
{
    public void Configure(EntityTypeBuilder<HostedLlmDailyUsage> builder)
    {
        builder.ToTable("HostedLlmDailyUsages");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UsageDateUtc).HasColumnType("date").IsRequired();
        builder.Property(x => x.Model).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ReservedRequests).IsRequired();
        builder.Property(x => x.FinalizedRequests).IsRequired();
        builder.Property(x => x.SuccessfulRequests).IsRequired();
        builder.Property(x => x.FailedRequests).IsRequired();
        builder.Property(x => x.InputTokens).IsRequired();
        builder.Property(x => x.OutputTokens).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.UsageDateUtc, x.Model })
            .IsUnique();
        builder.HasIndex(x => x.UsageDateUtc);
    }
}
