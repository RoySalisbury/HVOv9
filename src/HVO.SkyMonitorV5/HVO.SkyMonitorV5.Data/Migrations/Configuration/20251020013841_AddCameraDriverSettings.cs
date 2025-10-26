using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.SkyMonitorV5.Data.Migrations.Configuration
{
    /// <inheritdoc />
    public partial class AddCameraDriverSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "driver_settings_json",
                table: "camera_catalog",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "camera_catalog",
                keyColumn: "id",
                keyValue: 1,
                column: "driver_settings_json",
                value: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "driver_settings_json",
                table: "camera_catalog");
        }
    }
}
