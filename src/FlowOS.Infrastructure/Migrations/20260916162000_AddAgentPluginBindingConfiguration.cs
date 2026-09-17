using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations;

[DbContext(typeof(FlowOSDbContext))]
[Migration("20260916162000_AddAgentPluginBindingConfiguration")]
public partial class AddAgentPluginBindingConfiguration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "PluginBindings" ADD COLUMN IF NOT EXISTS "ConfigurationJson" jsonb;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ConfigurationJson",
            table: "PluginBindings");
    }
}
