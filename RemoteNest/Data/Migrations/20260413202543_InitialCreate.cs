using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RemoteNest.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConnectionProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Group = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EncryptedPassword = table.Column<string>(type: "TEXT", nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ScreenWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    ScreenHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    FullScreen = table.Column<bool>(type: "INTEGER", nullable: false),
                    ColorDepth = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    RedirectClipboard = table.Column<bool>(type: "INTEGER", nullable: false),
                    RedirectDrives = table.Column<bool>(type: "INTEGER", nullable: false),
                    RedirectPrinters = table.Column<bool>(type: "INTEGER", nullable: false),
                    RedirectAudio = table.Column<bool>(type: "INTEGER", nullable: false),
                    UseNetworkLevelAuth = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastConnectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConnectionCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionProfiles_Group",
                table: "ConnectionProfiles",
                column: "Group");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionProfiles_Host",
                table: "ConnectionProfiles",
                column: "Host");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionProfiles_Name",
                table: "ConnectionProfiles",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectionProfiles");
        }
    }
}
