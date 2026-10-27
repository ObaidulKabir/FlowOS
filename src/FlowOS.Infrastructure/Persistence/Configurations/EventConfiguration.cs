using FlowOS.Agents.Events;
using FlowOS.Events.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public class EventConfiguration : IEntityTypeConfiguration<DomainEvent>
{
    /// <summary>
    /// CR-10c: database-assigned, strictly increasing position of the event in the change feed.
    /// A shadow property so the domain model does not depend on storage.
    /// </summary>
    public const string SequenceProperty = "Sequence";

    public void Configure(EntityTypeBuilder<DomainEvent> builder)
    {
        builder.ToTable("Events");

        // Use TPH Mapping for the abstract class DomainEvent
        builder.HasDiscriminator<string>("Discriminator")
            .HasValue<TaskCompleted>("TaskCompleted")
            .HasValue<AgentInsightGenerated>("AgentInsightGenerated")
            .HasValue<StandardEvent>("StandardEvent");

        builder.HasKey(e => e.EventId);

        builder.Property(e => e.EventType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.TenantId)
            .IsRequired();

        builder.Property(e => e.Metadata)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>()
            );

        builder.Property<long>(SequenceProperty)
            .ValueGeneratedOnAdd()
            .UseIdentityByDefaultColumn();

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex("TenantId", SequenceProperty);
    }
}
