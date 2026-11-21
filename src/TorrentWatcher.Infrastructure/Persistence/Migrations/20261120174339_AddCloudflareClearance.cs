using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorrentWatcher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudflareClearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloudflareClearanceCookie",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CloudflareClearanceExpiresAtUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CloudflareClearanceObtainedAtUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudflareLastError",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudflareUserAgent",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CloudflareClearanceCookie", "CloudflareClearanceExpiresAtUtc", "CloudflareClearanceObtainedAtUtc", "CloudflareLastError", "CloudflareUserAgent" },
                values: new object[] { null, null, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloudflareClearanceCookie",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "CloudflareClearanceExpiresAtUtc",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "CloudflareClearanceObtainedAtUtc",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "CloudflareLastError",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "CloudflareUserAgent",
                table: "AppSettings");
        }
    }
}
