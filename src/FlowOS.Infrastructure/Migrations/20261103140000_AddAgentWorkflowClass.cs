using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <summary>
    /// ADR-006 Phase 4A: Introduces AgentWorkflowClasses table — the declarative
    /// multi-agent coordination plan blueprint.
    ///
    /// Mirrors WorkflowClass in structure:
    ///   • Definition column stores AgentWorkflowBlueprint serialised as jsonb.
    ///   • Lifecycle: Draft → Published → (Deprecated | Abandoned).
    ///   • PreviousVersionId forms a singly-linked lineage chain.
    ///
    /// No engine changes in this migration — the blueprint is stored and versioned;
    /// dispatch machinery arrives in Phase 4B.
    /// </summary>
    public partial class AddAgentWorkflowClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentWorkflowClasses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PreviousVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeLog = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DeprecationReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DeprecationMigrationTargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    FlowOsVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "1.1.0"),
                    Definition = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentWorkflowClasses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentWorkflowClasses_TenantId",
                table: "AgentWorkflowClasses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentWorkflowClasses_PreviousVersionId",
                table: "AgentWorkflowClasses",
                column: "PreviousVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentWorkflowClasses");
        }
    }
}
