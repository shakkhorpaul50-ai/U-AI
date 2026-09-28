using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UAI.Data.Migrations
{
    /// <inheritdoc />
    public partial class Quotas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserQuotas",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    DailyLimit = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserQuotas", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserQuotas_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserUsages",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserUsages", x => new { x.UserId, x.Date });
                    table.ForeignKey(
                        name: "FK_UserUsages_AspNetUsers_UserId",
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
                name: "UserQuotas");

            migrationBuilder.DropTable(
                name: "UserUsages");
        }
    }
}
