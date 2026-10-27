using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventChangeFeedSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "Events",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.CreateIndex(
                name: "IX_Events_TenantId_Sequence",
                table: "Events",
                columns: new[] { "TenantId", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Events_TenantId_Sequence",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "Events");
        }
    }
}
