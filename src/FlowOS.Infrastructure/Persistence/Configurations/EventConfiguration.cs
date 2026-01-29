using FlowOS.Events.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class EventConfiguration : IEntityTypeConfiguration<DomainEvent>
{
    public void Configure(EntityTypeBuilder<DomainEvent> builder)
    {
        builder.ToTable("Events");

        builder.HasKey(e => e.EventId);

        builder.Property(e => e.EventType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.TenantId)
            .IsRequired();

        // Metadata as JSONB
        // Using System.Text.Json to serialize dictionary
        builder.Property(e => e.Metadata)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions)null) ?? new Dictionary<string, string>()
            );

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => e.Timestamp);
    }
}
