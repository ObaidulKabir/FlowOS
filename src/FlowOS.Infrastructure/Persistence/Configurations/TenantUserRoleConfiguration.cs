using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class TenantUserRoleConfiguration : IEntityTypeConfiguration<TenantUserRole>
{
    public void Configure(EntityTypeBuilder<TenantUserRole> builder)
    {
        builder.ToTable("TenantUserRoles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.TenantUserId).IsRequired();
        builder.Property(x => x.RoleId).IsRequired();
        builder.Property(x => x.AssignedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.TenantUserId, x.RoleId })
            .IsUnique();

        builder.HasIndex(x => x.TenantId);
    }
}
