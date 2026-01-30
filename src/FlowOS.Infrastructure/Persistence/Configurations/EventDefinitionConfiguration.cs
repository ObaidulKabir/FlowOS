using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class EventDefinitionConfiguration : IEntityTypeConfiguration<EventDefinition>
{
    public void Configure(EntityTypeBuilder<EventDefinition> builder)
    {
        builder.HasKey(e => new { e.EventId, e.TenantId });

        builder.Property(e => e.EventId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Category)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<string>();
    }
}
