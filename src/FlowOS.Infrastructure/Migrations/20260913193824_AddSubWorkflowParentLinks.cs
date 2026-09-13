using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubWorkflowParentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParentStepId",
                table: "WorkflowInstances",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentWorkflowInstanceId",
                table: "WorkflowInstances",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_TenantId_ParentWorkflowInstanceId_ParentS~",
                table: "WorkflowInstances",
                columns: new[] { "TenantId", "ParentWorkflowInstanceId", "ParentStepId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstances_TenantId_ParentWorkflowInstanceId_ParentS~",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "ParentStepId",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "ParentWorkflowInstanceId",
                table: "WorkflowInstances");
        }
    }
}
