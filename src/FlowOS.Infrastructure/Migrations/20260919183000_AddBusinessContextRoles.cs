using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations;

[DbContext(typeof(FlowOSDbContext))]
[Migration("20260919183000_AddBusinessContextRoles")]
public partial class AddBusinessContextRoles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "WorkflowInstances" ADD COLUMN IF NOT EXISTS "RoleAssignments" text NOT NULL DEFAULT '{}';
            ALTER TABLE "WorkflowDefinitions" ADD COLUMN IF NOT EXISTS "BusinessRoles" text NOT NULL DEFAULT '[]';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RoleAssignments",
            table: "WorkflowInstances");

        migrationBuilder.DropColumn(
            name: "BusinessRoles",
            table: "WorkflowDefinitions");
    }
}
