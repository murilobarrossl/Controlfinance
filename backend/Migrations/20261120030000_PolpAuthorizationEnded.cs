using System;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261120030000_PolpAuthorizationEnded")]
    public partial class PolpAuthorizationEnded : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(name: "AuthorizationEndedAt", table: "PolpIntegrations", type: "timestamp with time zone", nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AuthorizationEndedAt", table: "PolpIntegrations");
        }
    }
}
