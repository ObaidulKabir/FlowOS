using FlowOS.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOS.Notifications.Infrastructure.Persistence;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        // Try fully qualifying to ensure we are using the right extension method
        Microsoft.EntityFrameworkCore.RelationalEntityTypeBuilderExtensions.ToTable(builder, "Notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.TenantId).IsRequired();
        builder.Property(n => n.EventType).IsRequired().HasMaxLength(100);
        builder.Property(n => n.Message).IsRequired().HasMaxLength(500);
        builder.Property(n => n.Severity).IsRequired().HasMaxLength(20);
        builder.Property(n => n.CreatedAt).IsRequired();

        builder.HasIndex(n => n.TenantId);
        builder.HasIndex(n => n.CreatedAt);
    }
}
