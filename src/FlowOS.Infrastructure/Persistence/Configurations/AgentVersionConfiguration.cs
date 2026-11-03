using FlowOS.Domain.Entities.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace FlowOS.Infrastructure.Persistence.Configurations;

public sealed class AgentVersionConfiguration : IEntityTypeConfiguration<AgentVersion>
{
    public void Configure(EntityTypeBuilder<AgentVersion> builder)
    {
        builder.ToTable("AgentVersions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.AgentId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.Label).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Role).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ProviderAlias).IsRequired().HasMaxLength(200);
        builder.Property(x => x.PromptAlias).HasMaxLength(200);
        builder.Property(x => x.AutoCommitThreshold);
        builder.Property(x => x.AutonomyLevel).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.IsDeprecated).IsRequired();
        builder.Property(x => x.PublishedAtUtc).IsRequired();
        builder.Property(x => x.DeprecatedAtUtc);
        builder.Property(x => x.PublishedBy).IsRequired().HasMaxLength(200);

        // Stored as JSON arrays
        builder.Property(x => x.ToolAliases)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new()))
            .HasColumnType("jsonb");

        builder.Property(x => x.AllowedEvents)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : (IReadOnlyList<string>?)JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null))
            .HasColumnType("jsonb");

        builder.Property(x => x.CapabilityTags)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new()))
            .HasColumnType("jsonb");

        // Unique version per tenant+agentId
        builder.HasIndex(x => new { x.TenantId, x.AgentId, x.Version })
            .IsUnique()
            .HasDatabaseName("IX_AgentVersions_TenantAgentVersion");

        // Fast lookup of latest non-deprecated versions for capability matching
        builder.HasIndex(x => new { x.TenantId, x.AgentId, x.IsDeprecated })
            .HasDatabaseName("IX_AgentVersions_TenantAgentDeprecated");

        // Tenant isolation — no cross-tenant queries
        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("IX_AgentVersions_TenantId");
    }
}
