using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <summary>
    /// ADR-005 Phase 3: Adds outcome fields to TeamExecutions.
    ///
    /// CommittedEvent  — the workflow event the agent committed (e.g. "RISK_LOW").
    /// Confidence      — agent confidence score [0.0, 1.0].
    /// ActorAgentId    — the specific agent that acted on the slot.
    ///
    /// These three columns are written once by TeamExecution.Complete() and are
    /// the primary signal the External Orchestrator Agent reads via the
    /// get_team_execution_outcome MCP tool to make its next sequencing decision.
    /// </summary>
    public partial class AddTeamExecutionOutcomeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommittedEvent",
                table: "TeamExecutions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Confidence",
                table: "TeamExecutions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorAgentId",
                table: "TeamExecutions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommittedEvent",
                table: "TeamExecutions");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "TeamExecutions");

            migrationBuilder.DropColumn(
                name: "ActorAgentId",
                table: "TeamExecutions");
        }
    }
}
