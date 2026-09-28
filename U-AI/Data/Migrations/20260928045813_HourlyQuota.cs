using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UAI.Data.Migrations
{
    /// <inheritdoc />
    public partial class HourlyQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HourlyLimit",
                table: "UserQuotas",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserHourlyUsages",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Hour = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserHourlyUsages", x => new { x.UserId, x.Hour });
                    table.ForeignKey(
                        name: "FK_UserHourlyUsages_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserHourlyUsages");

            migrationBuilder.DropColumn(
                name: "HourlyLimit",
                table: "UserQuotas");
        }
    }
}
