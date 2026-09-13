using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCapabilityBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveStepIds",
                table: "WorkflowInstances",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CompletedParallelStepIds",
                table: "WorkflowInstances",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WebhookSigningSecret",
                table: "Tenants",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeadLetter",
                table: "OutboxMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxRetries",
                table: "OutboxMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryUtc",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CapabilityBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapabilityName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Transport = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EndpointUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AuthRef = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RequestSchemaVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ResponseSchemaVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RetryPolicy = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    TimeoutMs = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapabilityBindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowActionExecutionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    TriggerPhase = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Target = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExecutedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    RequestPayloadSnippet = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ResponseSnippet = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    OutboxMessageId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowActionExecutionLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_IsDeadLetter_ProcessedOnUtc_NextRetryUtc",
                table: "OutboxMessages",
                columns: new[] { "IsDeadLetter", "ProcessedOnUtc", "NextRetryUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityBindings_IsEnabled",
                table: "CapabilityBindings",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityBindings_TenantId_CapabilityName",
                table: "CapabilityBindings",
                columns: new[] { "TenantId", "CapabilityName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_TenantId_OperationName_IdempotencyKey",
                table: "IdempotencyRecords",
                columns: new[] { "TenantId", "OperationName", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_UpdatedAtUtc",
                table: "IdempotencyRecords",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionExecutionLogs_TenantId",
                table: "WorkflowActionExecutionLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionExecutionLogs_TenantId_WorkflowInstanceId_Exe~",
                table: "WorkflowActionExecutionLogs",
                columns: new[] { "TenantId", "WorkflowInstanceId", "ExecutedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionExecutionLogs_WorkflowInstanceId",
                table: "WorkflowActionExecutionLogs",
                column: "WorkflowInstanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CapabilityBindings");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "WorkflowActionExecutionLogs");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_IsDeadLetter_ProcessedOnUtc_NextRetryUtc",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ActiveStepIds",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "CompletedParallelStepIds",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "WebhookSigningSecret",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "IsDeadLetter",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "MaxRetries",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "NextRetryUtc",
                table: "OutboxMessages");
        }
    }
}
