using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public sealed class DistributedLeaseConfiguration : IEntityTypeConfiguration<DistributedLease>
{
    public void Configure(EntityTypeBuilder<DistributedLease> builder)
    {
        builder.ToTable("DistributedLeases");
        builder.HasKey(x => x.LeaseKey);

        builder.Property(x => x.LeaseKey).HasMaxLength(300);
        builder.Property(x => x.OwnerId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.AcquiredAtUtc).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).IsRequired();

        builder.HasIndex(x => x.ExpiresAtUtc);
    }
}
