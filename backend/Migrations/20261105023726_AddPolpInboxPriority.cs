using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPolpInboxPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "PolpWebhookEvents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PolpWebhookEvents_Status_Priority_NextAttemptAt",
                table: "PolpWebhookEvents",
                columns: new[] { "Status", "Priority", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PolpWebhookEvents_Status_Priority_NextAttemptAt",
                table: "PolpWebhookEvents");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "PolpWebhookEvents");
        }
    }
}
