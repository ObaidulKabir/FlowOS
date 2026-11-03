using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentOrchestrationPhase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------------------------------------------------------------
            // AgentVersions
            // Immutable, versioned snapshots of agent definitions.
            // ADR-002: separate from IAM Teams.
            // ADR-010: autonomy is policy-controlled, not prompt-controlled.
            // ---------------------------------------------------------------
            migrationBuilder.CreateTable(
                name: "AgentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Role = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderAlias = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PromptAlias = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ToolAliases = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    AllowedEvents = table.Column<string>(type: "jsonb", nullable: true),
                    CapabilityTags = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    AutonomyLevel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AutoCommitThreshold = table.Column<double>(type: "double precision", nullable: true),
                    IsDeprecated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeprecatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentVersions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentVersions_TenantAgentVersion",
                table: "AgentVersions",
                columns: new[] { "TenantId", "AgentId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentVersions_TenantAgentDeprecated",
                table: "AgentVersions",
                columns: new[] { "TenantId", "AgentId", "IsDeprecated" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentVersions_TenantId",
                table: "AgentVersions",
                column: "TenantId");

            // ---------------------------------------------------------------
            // AgentTeams
            // Orchestration collaboration containers for AI agents.
            // IMPORTANT: table name "AgentTeams" — NEVER "Teams" (IAM domain).
            // ADR-002: Team (IAM) and AgentTeam (orchestration) are permanently separate.
            // ---------------------------------------------------------------
            migrationBuilder.CreateTable(
                name: "AgentTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false, defaultValue: ""),
                    DefaultObjective = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Slots = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    CapabilityTags = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    MaxNestingDepth = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    MaxAutonomyLevel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTeams", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTeams_TenantName",
                table: "AgentTeams",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTeams_TenantStatus",
                table: "AgentTeams",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AgentTeams");
            migrationBuilder.DropTable(name: "AgentVersions");
        }
    }
}
