using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlTime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsageSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsageSessions_StartedAtUtc",
                table: "UsageSessions",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsageSessions");
        }
    }
}
