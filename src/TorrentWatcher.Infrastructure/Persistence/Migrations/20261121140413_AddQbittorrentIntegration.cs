using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorrentWatcher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQbittorrentIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AppliedAtUtc",
                table: "TrackedTorrents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppliedInfoHashV1",
                table: "TrackedTorrents",
                type: "TEXT",
                fixedLength: true,
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplyError",
                table: "TrackedTorrents",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupersededInfoHashV1",
                table: "TrackedTorrents",
                type: "TEXT",
                fixedLength: true,
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "QbittorrentAutoApply",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "QbittorrentLastError",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QbittorrentPasswordProtected",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "QbittorrentUrl",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "QbittorrentUsername",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "QbittorrentVerifiedAtUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "QbittorrentAutoApply", "QbittorrentLastError", "QbittorrentPasswordProtected", "QbittorrentUrl", "QbittorrentUsername", "QbittorrentVerifiedAtUtc" },
                values: new object[] { false, null, "", "", "", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppliedAtUtc",
                table: "TrackedTorrents");

            migrationBuilder.DropColumn(
                name: "AppliedInfoHashV1",
                table: "TrackedTorrents");

            migrationBuilder.DropColumn(
                name: "ApplyError",
                table: "TrackedTorrents");

            migrationBuilder.DropColumn(
                name: "SupersededInfoHashV1",
                table: "TrackedTorrents");

            migrationBuilder.DropColumn(
                name: "QbittorrentAutoApply",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "QbittorrentLastError",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "QbittorrentPasswordProtected",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "QbittorrentUrl",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "QbittorrentUsername",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "QbittorrentVerifiedAtUtc",
                table: "AppSettings");
        }
    }
}
