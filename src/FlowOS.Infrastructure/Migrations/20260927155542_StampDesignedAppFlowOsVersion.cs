using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StampDesignedAppFlowOsVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FlowOsVersion",
                table: "WorkflowContextBindingRevisions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "1.1.0");

            migrationBuilder.AddColumn<string>(
                name: "FlowOsVersion",
                table: "WorkflowClasses",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "1.1.0");

            migrationBuilder.AddColumn<string>(
                name: "FlowOsVersion",
                table: "PluginBindings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "1.1.0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FlowOsVersion",
                table: "WorkflowContextBindingRevisions");

            migrationBuilder.DropColumn(
                name: "FlowOsVersion",
                table: "WorkflowClasses");

            migrationBuilder.DropColumn(
                name: "FlowOsVersion",
                table: "PluginBindings");
        }
    }
}
