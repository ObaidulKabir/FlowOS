using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableAgentExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentExecutionRecords",
                columns: table => new
                {
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DecisionOutcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderAlias = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PromptAlias = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RuntimeIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RuntimeVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    WorkflowDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkflowDefinitionVersion = table.Column<int>(type: "integer", nullable: true),
                    ContextBindingId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContextBindingRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContextVersion = table.Column<long>(type: "bigint", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SanitizedFailure = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    SuggestedEvent = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: true),
                    WasCommitted = table.Column<bool>(type: "boolean", nullable: false),
                    WasParked = table.Column<bool>(type: "boolean", nullable: false),
                    ParkReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ObservedOutcome = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OutcomeMatchedSuggestion = table.Column<bool>(type: "boolean", nullable: true),
                    OutcomeEvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WasOverridden = table.Column<bool>(type: "boolean", nullable: false),
                    OverrideActor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OverrideEvent = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OverrideReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OverriddenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentExecutionRecords", x => x.ExecutionId);
                });

            migrationBuilder.CreateTable(
                name: "AgentTaskJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAgentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Objective = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AllowAutoCommit = table.Column<bool>(type: "boolean", nullable: false),
                    RequireAgentActor = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActiveKey = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Claimant = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DistributedLeases",
                columns: table => new
                {
                    LeaseKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AcquiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DistributedLeases", x => x.LeaseKey);
                });

            migrationBuilder.CreateTable(
                name: "HostedLlmDailyUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsageDateUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReservedRequests = table.Column<int>(type: "integer", nullable: false),
                    FinalizedRequests = table.Column<int>(type: "integer", nullable: false),
                    SuccessfulRequests = table.Column<int>(type: "integer", nullable: false),
                    FailedRequests = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedLlmDailyUsages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentExecutionRecords_JobId",
                table: "AgentExecutionRecords",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentExecutionRecords_Tenant_Instance_Time",
                table: "AgentExecutionRecords",
                columns: new[] { "TenantId", "WorkflowInstanceId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentExecutionRecords_Tenant_Time",
                table: "AgentExecutionRecords",
                columns: new[] { "TenantId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskJobs_ActiveKey",
                table: "AgentTaskJobs",
                column: "ActiveKey",
                unique: true,
                filter: "\"ActiveKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskJobs_ExpiredClaims",
                table: "AgentTaskJobs",
                columns: new[] { "Status", "ClaimExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskJobs_Polling",
                table: "AgentTaskJobs",
                columns: new[] { "Status", "DueAtUtc", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskJobs_TenantId_WorkflowInstanceId_StepId",
                table: "AgentTaskJobs",
                columns: new[] { "TenantId", "WorkflowInstanceId", "StepId" });

            migrationBuilder.CreateIndex(
                name: "IX_DistributedLeases_ExpiresAtUtc",
                table: "DistributedLeases",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_HostedLlmDailyUsages_TenantId_UsageDateUtc_Model",
                table: "HostedLlmDailyUsages",
                columns: new[] { "TenantId", "UsageDateUtc", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedLlmDailyUsages_UsageDateUtc",
                table: "HostedLlmDailyUsages",
                column: "UsageDateUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentExecutionRecords");

            migrationBuilder.DropTable(
                name: "AgentTaskJobs");

            migrationBuilder.DropTable(
                name: "DistributedLeases");

            migrationBuilder.DropTable(
                name: "HostedLlmDailyUsages");
        }
    }
}
