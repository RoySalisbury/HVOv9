using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.SkyMonitorV5.Data.Migrations.Configuration
{
    /// <inheritdoc />
    public partial class AddRigRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "revision",
                table: "rig_catalog_entry",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.UpdateData(
                table: "rig_catalog_entry",
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
                table: "rig_catalog_entry");
        }
    }
}
