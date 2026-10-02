using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Confidence",
                table: "Events",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderName",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StepId",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedEvent",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowInstanceId",
                table: "Events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Confidence",
                table: "AgentInsights",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderName",
                table: "AgentInsights",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StepId",
                table: "AgentInsights",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedEvent",
                table: "AgentInsights",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConversationMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepId = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessages", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationMessages");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ProviderName",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "StepId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "SuggestedEvent",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "WorkflowInstanceId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "AgentInsights");

            migrationBuilder.DropColumn(
                name: "ProviderName",
                table: "AgentInsights");

            migrationBuilder.DropColumn(
                name: "StepId",
                table: "AgentInsights");

            migrationBuilder.DropColumn(
                name: "SuggestedEvent",
                table: "AgentInsights");
        }
    }
}
