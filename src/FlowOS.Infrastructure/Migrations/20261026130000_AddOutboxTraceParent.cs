using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOS.Infrastructure.Migrations
{
    /// <summary>
    /// CR-07: store the producer's W3C traceparent with each outbox message so the consumer
    /// span joins the same trace. Nullable column; existing rows keep NULL.
    /// </summary>
    [DbContext(typeof(FlowOSDbContext))]
    [Migration("20261026130000_AddOutboxTraceParent")]
    public partial class AddOutboxTraceParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "OutboxMessages",
                type: "character varying(55)",
                maxLength: 55,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "OutboxMessages");
        }
    }
}
