using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorrentWatcher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    RutrackerCookie = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CookieVerifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CookieLastError = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ScanIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoScanEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequestDelayMs = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestTimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    LastScanAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackedTorrents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TopicId = table.Column<int>(type: "INTEGER", nullable: false),
                    TopicUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    InfoHashV1 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 40, nullable: false),
                    TorrentSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TorrentFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    TorrentFileSha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    TorrentFileDownloadedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCheckOutcome = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    LastChangedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedTorrents", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "Id", "AutoScanEnabled", "CookieLastError", "CookieVerifiedAtUtc", "LastScanAtUtc", "RequestDelayMs", "RequestTimeoutSeconds", "RutrackerCookie", "ScanIntervalMinutes", "UpdatedAtUtc" },
                values: new object[] { 1, true, null, null, null, 1500, 30, "", 60, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedTorrents_TopicId",
                table: "TrackedTorrents",
                column: "TopicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "TrackedTorrents");
        }
    }
}
