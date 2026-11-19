using System;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261120040000_UserInactivity")]
    public partial class UserInactivity : Migration
    {
        // A atividade real de contas existentes é desconhecida: começam a contar da aplicação desta
        // migration, o que só adia (nunca antecipa) a exclusão por inatividade.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(name: "LastActivityAt", table: "Users", type: "timestamp with time zone", nullable: false, defaultValueSql: "now()");
            migrationBuilder.AddColumn<int>(name: "InactivityNoticeStage", table: "Users", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<DateTime>(name: "InactivityNoticeEpoch", table: "Users", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "InactivityDeletionAt", table: "Users", type: "timestamp with time zone", nullable: true);
            migrationBuilder.CreateIndex(name: "IX_Users_LastActivityAt", table: "Users", column: "LastActivityAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Users_LastActivityAt", table: "Users");
            migrationBuilder.DropColumn(name: "LastActivityAt", table: "Users");
            migrationBuilder.DropColumn(name: "InactivityNoticeStage", table: "Users");
            migrationBuilder.DropColumn(name: "InactivityNoticeEpoch", table: "Users");
            migrationBuilder.DropColumn(name: "InactivityDeletionAt", table: "Users");        }
    }
}
