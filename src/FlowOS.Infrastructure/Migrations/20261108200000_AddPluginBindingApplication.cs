using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <summary>
    /// Applications (3/4): a prompt or agent-profile binding may belong to a tenant application.
    ///
    /// The alias stays unique among the tenant-shared bindings (ApplicationId NULL) and becomes unique
    /// separately within each application, so an application can define its own prompt or agent with the
    /// same alias as a shared one. Every existing binding stays tenant-shared; nothing resolves
    /// differently until a tenant creates an application-scoped binding.
    /// </summary>
    public partial class AddPluginBindingApplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PluginBindings_TenantId_BindingType_SourceName",
                table: "PluginBindings");

            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationId",
                table: "PluginBindings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PluginBindings_ApplicationId",
                table: "PluginBindings",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_PluginBindings_TenantId_ApplicationId_BindingType_SourceName",
                table: "PluginBindings",
                columns: new[] { "TenantId", "ApplicationId", "BindingType", "SourceName" },
                unique: true,
                filter: "\"ApplicationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PluginBindings_TenantId_BindingType_SourceName",
                table: "PluginBindings",
                columns: new[] { "TenantId", "BindingType", "SourceName" },
                unique: true,
                filter: "\"ApplicationId\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PluginBindings_TenantApplications_ApplicationId",
                table: "PluginBindings",
                column: "ApplicationId",
                principalTable: "TenantApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PluginBindings_TenantApplications_ApplicationId",
                table: "PluginBindings");

            migrationBuilder.DropIndex(
                name: "IX_PluginBindings_ApplicationId",
                table: "PluginBindings");

            migrationBuilder.DropIndex(
                name: "IX_PluginBindings_TenantId_ApplicationId_BindingType_SourceName",
                table: "PluginBindings");

            migrationBuilder.DropIndex(
                name: "IX_PluginBindings_TenantId_BindingType_SourceName",
                table: "PluginBindings");

            // Application-scoped bindings cannot be represented after the column is dropped; remove them
            // so the restored unique index on (TenantId, BindingType, SourceName) can be created.
            migrationBuilder.Sql("DELETE FROM \"PluginBindings\" WHERE \"ApplicationId\" IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "PluginBindings");

            migrationBuilder.CreateIndex(
                name: "IX_PluginBindings_TenantId_BindingType_SourceName",
                table: "PluginBindings",
                columns: new[] { "TenantId", "BindingType", "SourceName" },
                unique: true);
        }
    }
}
