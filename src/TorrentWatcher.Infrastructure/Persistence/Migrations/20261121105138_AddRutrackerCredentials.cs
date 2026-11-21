using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorrentWatcher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRutrackerCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RutrackerLogin",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RutrackerPasswordProtected",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "RutrackerLogin", "RutrackerPasswordProtected" },
                values: new object[] { "", "" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RutrackerLogin",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "RutrackerPasswordProtected",
                table: "AppSettings");
        }
    }
}
