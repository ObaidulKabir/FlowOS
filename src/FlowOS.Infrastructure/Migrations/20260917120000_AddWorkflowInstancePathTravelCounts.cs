using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations;

[DbContext(typeof(FlowOSDbContext))]
[Migration("20260917120000_AddWorkflowInstancePathTravelCounts")]
public partial class AddWorkflowInstancePathTravelCounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "WorkflowInstances" ADD COLUMN IF NOT EXISTS "PathTravelCounts" text NOT NULL DEFAULT '{}';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PathTravelCounts",
            table: "WorkflowInstances");
    }
}
