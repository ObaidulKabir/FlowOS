using FlowOS.Domain.Entities.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace FlowOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// ADR-002: AgentTeam maps to the "AgentTeams" table — never "Teams".
/// "Teams" is reserved for the IAM Team entity (FlowOS.Domain.Entities.Team).
/// </summary>
public sealed class AgentTeamConfiguration : IEntityTypeConfiguration<AgentTeam>
{
    public void Configure(EntityTypeBuilder<AgentTeam> builder)
    {
        builder.ToTable("AgentTeams");  // NEVER "Teams" — that belongs to the IAM domain
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.DefaultObjective).HasMaxLength(1000);
        builder.Property(x => x.MaxNestingDepth).IsRequired();
        builder.Property(x => x.MaxAutonomyLevel).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc);
        builder.Property(x => x.ActivatedAtUtc);
        builder.Property(x => x.ArchivedAtUtc);

        // Slots stored as JSONB — each slot is a value object with no identity of its own
        builder.Property(x => x.Slots)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => (IReadOnlyList<AgentTeamSlot>)(JsonSerializer.Deserialize<List<AgentTeamSlot>>(v, (JsonSerializerOptions?)null) ?? new()))
            .HasColumnType("jsonb");

        // Capability tags stored as JSONB for fast GIN-index queries
        builder.Property(x => x.CapabilityTags)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new()))
            .HasColumnType("jsonb");

        // Unique name per tenant (teams within a tenant must have distinct names)
        builder.HasIndex(x => new { x.TenantId, x.Name })
            .IsUnique()
            .HasDatabaseName("IX_AgentTeams_TenantName");

        // Fast status filtering for orchestration queries
        builder.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("IX_AgentTeams_TenantStatus");
    }
}
