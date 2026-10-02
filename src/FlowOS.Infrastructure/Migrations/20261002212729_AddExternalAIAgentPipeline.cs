using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalAIAgentPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExternalAIAgentAutoPilot",
                table: "Tenants",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExternalAIAgentEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalAIAgentProfileId",
                table: "Tenants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentPromptAuditRecords",
                columns: table => new
                {
                    AuditId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    StepId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProviderAlias = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: true),
                    UserPrompt = table.Column<string>(type: "text", nullable: true),
                    RawRequestPayload = table.Column<string>(type: "text", nullable: false),
                    RawResponsePayload = table.Column<string>(type: "text", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    Iteration = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentPromptAuditRecords", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "ExternalAgentChangeRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceOutboxMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    LeasedByAgent = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LeasedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    NextRetryUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalAgentChangeRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalAgentPlanRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentProfileId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlanStepsJson = table.Column<string>(type: "text", nullable: false),
                    PlanVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalAgentPlanRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalAgentPlanStepRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StepIndex = table.Column<int>(type: "integer", nullable: false),
                    ToolName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ToolArgsJson = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DependsOnJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultSnapshotJson = table.Column<string>(type: "character varying(65535)", maxLength: 65535, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalAgentPlanStepRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentPromptAuditRecords_ExecutionId",
                table: "AgentPromptAuditRecords",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPromptAuditRecords_Tenant_RecordedAt",
                table: "AgentPromptAuditRecords",
                columns: new[] { "TenantId", "RecordedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AgentPromptAuditRecords_WorkflowInstanceId",
                table: "AgentPromptAuditRecords",
                column: "WorkflowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgentChangeRecords_LeasedUntilUtc",
                table: "ExternalAgentChangeRecords",
                column: "LeasedUntilUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgentChangeRecords_Status_NextRetryUtc",
                table: "ExternalAgentChangeRecords",
                columns: new[] { "Status", "NextRetryUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgentChangeRecords_Tenant_CreatedAt_Id",
                table: "ExternalAgentChangeRecords",
                columns: new[] { "TenantId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgentPlanRecords_ChangeId",
                table: "ExternalAgentPlanRecords",
                column: "ChangeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgentPlanRecords_TenantId",
                table: "ExternalAgentPlanRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAgentPlanStepRecords_PlanId_StepIndex",
                table: "ExternalAgentPlanStepRecords",
                columns: new[] { "PlanId", "StepIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentPromptAuditRecords");

            migrationBuilder.DropTable(
                name: "ExternalAgentChangeRecords");

            migrationBuilder.DropTable(
                name: "ExternalAgentPlanRecords");

            migrationBuilder.DropTable(
                name: "ExternalAgentPlanStepRecords");

            migrationBuilder.DropColumn(
                name: "ExternalAIAgentAutoPilot",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ExternalAIAgentEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ExternalAIAgentProfileId",
                table: "Tenants");
        }
    }
}
