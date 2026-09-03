using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationDetailsAndScopesToTenantApiKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicationName",
                table: "TenantApiKeys",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "Default Application");

            migrationBuilder.AddColumn<string>(
                name: "Environment",
                table: "TenantApiKeys",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Production");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "TenantApiKeys",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Scopes",
                table: "TenantApiKeys",
                type: "text",
                nullable: false,
                defaultValue: "[\"*\"]");

            migrationBuilder.CreateIndex(
                name: "IX_TenantApiKeys_TenantId_ApplicationName",
                table: "TenantApiKeys",
                columns: new[] { "TenantId", "ApplicationName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantApiKeys_TenantId_ApplicationName",
                table: "TenantApiKeys");

            migrationBuilder.DropColumn(
                name: "ApplicationName",
                table: "TenantApiKeys");

            migrationBuilder.DropColumn(
                name: "Environment",
                table: "TenantApiKeys");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "TenantApiKeys");

            migrationBuilder.DropColumn(
                name: "Scopes",
                table: "TenantApiKeys");
        }
    }
}
