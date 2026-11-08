using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class TenantApplicationConfiguration : IEntityTypeConfiguration<TenantApplication>
{
    public void Configure(EntityTypeBuilder<TenantApplication> builder)
    {
        builder.ToTable("TenantApplications");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(TenantApplication.NameMaxLength);

        builder.Property(a => a.Description)
            .HasMaxLength(TenantApplication.DescriptionMaxLength);

        builder.HasIndex(a => new { a.TenantId, a.Name }).IsUnique();
    }
}
