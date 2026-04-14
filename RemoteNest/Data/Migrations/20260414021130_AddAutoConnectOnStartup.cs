using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RemoteNest.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoConnectOnStartup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoConnectOnStartup",
                table: "ConnectionProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoConnectOnStartup",
                table: "ConnectionProfiles");
        }
    }
}
