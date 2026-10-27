using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowInstanceExternalReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                table: "WorkflowInstances",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSourceSystem",
                table: "WorkflowInstances",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_TenantId_ExternalSourceSystem_ExternalRef~",
                table: "WorkflowInstances",
                columns: new[] { "TenantId", "ExternalSourceSystem", "ExternalReference" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstances_TenantId_ExternalSourceSystem_ExternalRef~",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "ExternalSourceSystem",
                table: "WorkflowInstances");
        }
    }
}
