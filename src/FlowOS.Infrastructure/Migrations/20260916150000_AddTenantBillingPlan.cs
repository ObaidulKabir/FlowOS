using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FlowOSDbContext))]
    [Migration("20260916150000_AddTenantBillingPlan")]
    public partial class AddTenantBillingPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: production already had Tenants rows, and a previous
            // handwritten copy of this migration was not discovered by EF.
            migrationBuilder.Sql("""
                ALTER TABLE "Tenants" ADD COLUMN IF NOT EXISTS "BillingStatus" character varying(32) NOT NULL DEFAULT 'Active';
                ALTER TABLE "Tenants" ADD COLUMN IF NOT EXISTS "Plan" character varying(32) NOT NULL DEFAULT 'Managed';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingStatus",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Tenants");
        }
    }
}
