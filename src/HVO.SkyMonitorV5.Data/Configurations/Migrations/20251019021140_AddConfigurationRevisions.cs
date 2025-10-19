using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.SkyMonitorV5.Data.Configurations.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurationRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "revision",
                table: "system_setting",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "revision",
                table: "observatory_site",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.UpdateData(
                table: "observatory_site",
                keyColumn: "id",
                keyValue: 1,
                column: "revision",
                value: 1L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "revision",
                table: "system_setting");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "observatory_site");
        }
    }
}
