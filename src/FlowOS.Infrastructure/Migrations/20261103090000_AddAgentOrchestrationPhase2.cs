using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentOrchestrationPhase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------------------------------------------------------------
            // TeamExecutions
            // Runtime instance of an AgentTeam template.
            // ADR-001: mirrors WorkflowClass → WorkflowInstance pattern.
            // ADR-002: table is "TeamExecutions" — NEVER "Teams".
            // ADR-003: TeamExecution tracks which agents executed an AgentTask step.
            // ---------------------------------------------------------------
            migrationBuilder.CreateTable(
                name: "TeamExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkflowStepId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Objective = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    // SlotAssignments: { "roleName": "agentId", ... }
                    SlotAssignments = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamExecutions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamExecutions_TenantTeam",
                table: "TeamExecutions",
                columns: new[] { "TenantId", "AgentTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamExecutions_TenantInstanceStep",
                table: "TeamExecutions",
                columns: new[] { "TenantId", "WorkflowInstanceId", "WorkflowStepId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamExecutions_TenantStatus",
                table: "TeamExecutions",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TeamExecutions");
        }
    }
}
