using System;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261120050000_InactivityDeletionClaim")]
    public partial class InactivityDeletionClaim : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(name: "InactivityDeletionClaimedAt", table: "Users", type: "timestamp with time zone", nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "InactivityDeletionClaimedAt", table: "Users");
        }
    }
}
