using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <summary>
    /// Applications (2/4): a workflow class may belong to a tenant application. Existing workflow
    /// classes stay unassigned (ApplicationId NULL); nothing about how they run changes.
    /// </summary>
    public partial class AddWorkflowClassApplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationId",
                table: "WorkflowClasses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowClasses_ApplicationId",
                table: "WorkflowClasses",
                column: "ApplicationId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowClasses_TenantApplications_ApplicationId",
                table: "WorkflowClasses",
                column: "ApplicationId",
                principalTable: "TenantApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowClasses_TenantApplications_ApplicationId",
                table: "WorkflowClasses");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowClasses_ApplicationId",
                table: "WorkflowClasses");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "WorkflowClasses");
        }
    }
}
