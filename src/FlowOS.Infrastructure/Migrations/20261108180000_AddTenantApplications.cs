using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <summary>
    /// Applications (1/4): adds TenantApplications and links FlowOS API keys to them.
    ///
    /// Backfill: one application per distinct (TenantId, ApplicationName) label already on a tenant's
    /// keys, and those keys are linked to it. Keys still labelled with the "Default Application"
    /// placeholder are left unlinked. Every existing key keeps IsApplicationRestricted = false, so its
    /// access is unchanged; only keys issued for an application afterwards are marked restricted.
    /// </summary>
    public partial class AddTenantApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantApplications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantApplications_TenantId_Name",
                table: "TenantApplications",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationId",
                table: "TenantApiKeys",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsApplicationRestricted",
                table: "TenantApiKeys",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                INSERT INTO "TenantApplications" ("Id", "TenantId", "Name", "Description", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT gen_random_uuid(), k."TenantId", k."ApplicationName", NULL, MIN(k."CreatedAt"), MIN(k."CreatedAt")
                FROM "TenantApiKeys" k
                WHERE btrim(k."ApplicationName") <> '' AND k."ApplicationName" <> 'Default Application'
                GROUP BY k."TenantId", k."ApplicationName";
                """);

            migrationBuilder.Sql(
                """
                UPDATE "TenantApiKeys" k
                SET "ApplicationId" = a."Id"
                FROM "TenantApplications" a
                WHERE a."TenantId" = k."TenantId" AND a."Name" = k."ApplicationName";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TenantApiKeys_ApplicationId",
                table: "TenantApiKeys",
                column: "ApplicationId");

            migrationBuilder.AddForeignKey(
                name: "FK_TenantApiKeys_TenantApplications_ApplicationId",
                table: "TenantApiKeys",
                column: "ApplicationId",
                principalTable: "TenantApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TenantApiKeys_TenantApplications_ApplicationId",
                table: "TenantApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_TenantApiKeys_ApplicationId",
                table: "TenantApiKeys");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "TenantApiKeys");

            migrationBuilder.DropColumn(
                name: "IsApplicationRestricted",
                table: "TenantApiKeys");

            migrationBuilder.DropTable(
                name: "TenantApplications");
        }
    }
}
