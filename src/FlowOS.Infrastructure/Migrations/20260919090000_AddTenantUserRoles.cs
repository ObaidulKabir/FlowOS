using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations;

[DbContext(typeof(FlowOSDbContext))]
[Migration("20260919090000_AddTenantUserRoles")]
public partial class AddTenantUserRoles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS "TenantUserRoles" (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "TenantUserId" uuid NOT NULL,
                "RoleId" uuid NOT NULL,
                "AssignedAtUtc" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_TenantUserRoles" PRIMARY KEY ("Id")
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantUserRoles_TenantUserId_RoleId"
                ON "TenantUserRoles" ("TenantUserId", "RoleId");

            CREATE INDEX IF NOT EXISTS "IX_TenantUserRoles_TenantId"
                ON "TenantUserRoles" ("TenantId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TenantUserRoles");
    }
}
