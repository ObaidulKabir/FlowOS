using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowContextBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContextBindingRevisionId",
                table: "WorkflowDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceWorkflowClassId",
                table: "WorkflowDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StateMachineDefinitionId",
                table: "WorkflowDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Constraints",
                table: "StateTransition",
                type: "text",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_WorkflowInstances_Id_TenantId",
                table: "WorkflowInstances",
                columns: new[] { "Id", "TenantId" });

            migrationBuilder.CreateTable(
                name: "WorkflowContextBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NormalizedContextType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                    ActiveRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DraftRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowContextBindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowContextBindingRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SourceWorkflowClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceWorkflowClassVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Definition = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                    WorkflowDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    StateMachineDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupersededAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowContextBindingRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowContextBindingRevisions_StateMachineDefinitions_Sta~",
                        column: x => x.StateMachineDefinitionId,
                        principalTable: "StateMachineDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowContextBindingRevisions_WorkflowClasses_SourceWorkf~",
                        column: x => x.SourceWorkflowClassId,
                        principalTable: "WorkflowClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowContextBindingRevisions_WorkflowContextBindings_Bin~",
                        column: x => x.BindingId,
                        principalTable: "WorkflowContextBindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowContextBindingRevisions_WorkflowDefinitions_Workflo~",
                        column: x => x.WorkflowDefinitionId,
                        principalTable: "WorkflowDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowContextSnapshots",
                columns: table => new
                {
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextBindingRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalData = table.Column<string>(type: "text", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ExternalEntityId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BusinessMetadata = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowContextSnapshots", x => x.WorkflowInstanceId);
                    table.ForeignKey(
                        name: "FK_WorkflowContextSnapshots_WorkflowContextBindingRevisions_Co~",
                        column: x => x.ContextBindingRevisionId,
                        principalTable: "WorkflowContextBindingRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowContextSnapshots_WorkflowInstances_WorkflowInstance~",
                        columns: x => new { x.WorkflowInstanceId, x.TenantId },
                        principalTable: "WorkflowInstances",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_ContextBindingRevisionId",
                table: "WorkflowDefinitions",
                column: "ContextBindingRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_SourceWorkflowClassId",
                table: "WorkflowDefinitions",
                column: "SourceWorkflowClassId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_StateMachineDefinitionId",
                table: "WorkflowDefinitions",
                column: "StateMachineDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_StateMachineDefinitions_TenantId_EntityType_Version",
                table: "StateMachineDefinitions",
                columns: new[] { "TenantId", "EntityType", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowContextBindingRevisions_BindingId_Revision",
                table: "WorkflowContextBindingRevisions",
                columns: new[] { "BindingId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowContextBindingRevisions_BindingId_Status",
                table: "WorkflowContextBindingRevisions",
                columns: new[] { "BindingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowContextBindingRevisions_SourceWorkflowClassId",
                table: "WorkflowContextBindingRevisions",
                column: "SourceWorkflowClassId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowContextBindingRevisions_StateMachineDefinitionId",
                table: "WorkflowContextBindingRevisions",
                column: "StateMachineDefinitionId",
                unique: true,
                filter: "\"StateMachineDefinitionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowContextBindingRevisions_WorkflowDefinitionId",
                table: "WorkflowContextBindingRevisions",
                column: "WorkflowDefinitionId",
                unique: true,
                filter: "\"WorkflowDefinitionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowContextBindings_TenantId_NormalizedContextType",
                table: "WorkflowContextBindings",
                columns: new[] { "TenantId", "NormalizedContextType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowContextBindings_TenantId_NormalizedName",
                table: "WorkflowContextBindings",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowContextBindings_TenantId_Status",
                table: "WorkflowContextBindings",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowContextSnapshots_ContextBindingRevisionId",
                table: "WorkflowContextSnapshots",
                column: "ContextBindingRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowContextSnapshots_TenantId_ContextBindingRevisionId",
                table: "WorkflowContextSnapshots",
                columns: new[] { "TenantId", "ContextBindingRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowContextSnapshots_TenantId_SourceSystem_ExternalEnti~",
                table: "WorkflowContextSnapshots",
                columns: new[] { "TenantId", "SourceSystem", "ExternalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowContextSnapshots_WorkflowInstanceId_TenantId",
                table: "WorkflowContextSnapshots",
                columns: new[] { "WorkflowInstanceId", "TenantId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowDefinitions_StateMachineDefinitions_StateMachineDef~",
                table: "WorkflowDefinitions",
                column: "StateMachineDefinitionId",
                principalTable: "StateMachineDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowDefinitions_WorkflowClasses_SourceWorkflowClassId",
                table: "WorkflowDefinitions",
                column: "SourceWorkflowClassId",
                principalTable: "WorkflowClasses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowDefinitions_StateMachineDefinitions_StateMachineDef~",
                table: "WorkflowDefinitions");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowDefinitions_WorkflowClasses_SourceWorkflowClassId",
                table: "WorkflowDefinitions");

            migrationBuilder.DropTable(
                name: "WorkflowContextSnapshots");

            migrationBuilder.DropTable(
                name: "WorkflowContextBindingRevisions");

            migrationBuilder.DropTable(
                name: "WorkflowContextBindings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_WorkflowInstances_Id_TenantId",
                table: "WorkflowInstances");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowDefinitions_ContextBindingRevisionId",
                table: "WorkflowDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowDefinitions_SourceWorkflowClassId",
                table: "WorkflowDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowDefinitions_StateMachineDefinitionId",
                table: "WorkflowDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_StateMachineDefinitions_TenantId_EntityType_Version",
                table: "StateMachineDefinitions");

            migrationBuilder.DropColumn(
                name: "ContextBindingRevisionId",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "SourceWorkflowClassId",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "StateMachineDefinitionId",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "Constraints",
                table: "StateTransition");
        }
    }
}
