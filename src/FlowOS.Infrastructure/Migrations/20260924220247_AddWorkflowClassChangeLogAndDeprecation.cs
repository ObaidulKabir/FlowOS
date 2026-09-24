using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowClassChangeLogAndDeprecation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeLog",
                table: "WorkflowClasses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeprecationMigrationTargetId",
                table: "WorkflowClasses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeprecationReason",
                table: "WorkflowClasses",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangeLog",
                table: "WorkflowClasses");

            migrationBuilder.DropColumn(
                name: "DeprecationMigrationTargetId",
                table: "WorkflowClasses");

            migrationBuilder.DropColumn(
                name: "DeprecationReason",
                table: "WorkflowClasses");
        }
    }
}
