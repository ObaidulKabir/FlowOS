using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class WorkflowContextBindingConfiguration : IEntityTypeConfiguration<WorkflowContextBinding>
{
    public void Configure(EntityTypeBuilder<WorkflowContextBinding> builder)
    {
        builder.ToTable("WorkflowContextBindings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.ContextType).IsRequired().HasMaxLength(150);
        builder.Property(x => x.NormalizedContextType).IsRequired().HasMaxLength(150);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.NormalizedName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(WorkflowContextBindingStatus.Draft);
        builder.Property(x => x.ActiveRevisionId).IsRequired(false);
        builder.Property(x => x.DraftRevisionId).IsRequired(false);
        builder.Property(x => x.ArchivedAtUtc).IsRequired(false);
        builder.Property(x => x.UpdatedAtUtc).IsConcurrencyToken();

        builder.HasIndex(x => new { x.TenantId, x.NormalizedContextType }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.NormalizedName }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
    }
}
