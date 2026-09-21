using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations;

[DbContext(typeof(FlowOSDbContext))]
[Migration("20260921133000_AddAgentExecutionClaimant")]
public partial class AddAgentExecutionClaimant : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Claimant",
            table: "AgentExecutionRecords",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Claimant",
            table: "AgentExecutionRecords");
    }
}
