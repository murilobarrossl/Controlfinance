using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class RemoveInvestmentAuthorizationUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PolpIntegrations_UserId",
                table: "PolpIntegrations");

            migrationBuilder.DropColumn(
                name: "AuthorizationUrlEncrypted",
                table: "PolpInvestmentAuthorizations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorizationUrlEncrypted",
                table: "PolpInvestmentAuthorizations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolpIntegrations_UserId",
                table: "PolpIntegrations",
                column: "UserId");
        }
    }
}
