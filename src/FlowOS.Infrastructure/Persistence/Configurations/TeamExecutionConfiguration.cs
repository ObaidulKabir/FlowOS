using FlowOS.Domain.Entities.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace FlowOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// ADR-002: TeamExecution maps to the "TeamExecutions" table — never "Teams".
/// TeamExecution is the runtime instance of an AgentTeam template (ADR-001).
/// </summary>
public sealed class TeamExecutionConfiguration : IEntityTypeConfiguration<TeamExecution>
{
    public void Configure(EntityTypeBuilder<TeamExecution> builder)
    {
        builder.ToTable("TeamExecutions"); // NEVER "Teams" — that belongs to the IAM domain (ADR-002)
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.AgentTeamId).IsRequired();
        builder.Property(x => x.WorkflowInstanceId);
        builder.Property(x => x.WorkflowStepId).HasMaxLength(200);
        builder.Property(x => x.Objective).HasMaxLength(2000);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.StartedAtUtc);
        builder.Property(x => x.CompletedAtUtc);
        builder.Property(x => x.FailureReason).HasMaxLength(2000);

        // SlotAssignments stored as JSONB: { "roleName": "agentId", ... }
        builder.Property(x => x.SlotAssignments)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => (IReadOnlyDictionary<string, string>)(
                    JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)))
            .HasColumnType("jsonb");

        // Look up all executions for a team template (ADR-001 team composition audit)
        builder.HasIndex(x => new { x.TenantId, x.AgentTeamId })
            .HasDatabaseName("IX_TeamExecutions_TenantTeam");

        // Enforce one non-terminal execution per workflow step (prevents duplicate dispatches)
        builder.HasIndex(x => new { x.TenantId, x.WorkflowInstanceId, x.WorkflowStepId })
            .HasDatabaseName("IX_TeamExecutions_TenantInstanceStep");

        // Fast status queries for the Objective Orchestrator
        builder.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("IX_TeamExecutions_TenantStatus");
    }
}
