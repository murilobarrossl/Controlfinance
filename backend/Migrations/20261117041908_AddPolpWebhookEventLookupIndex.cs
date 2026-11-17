using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPolpWebhookEventLookupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PolpWebhookEvents_Event_ResourceId",
                table: "PolpWebhookEvents",
                columns: new[] { "Event", "ResourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PolpWebhookEvents_Event_ResourceId",
                table: "PolpWebhookEvents");
        }
    }
}
