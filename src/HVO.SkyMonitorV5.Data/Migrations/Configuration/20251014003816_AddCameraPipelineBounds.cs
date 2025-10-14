using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.SkyMonitorV5.Data.Migrations.Configuration
{
    /// <inheritdoc />
    public partial class AddCameraPipelineBounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "day_max_exposure_ms",
                table: "camera_pipeline_config",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "day_max_gain",
                table: "camera_pipeline_config",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "day_min_exposure_ms",
                table: "camera_pipeline_config",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "day_min_gain",
                table: "camera_pipeline_config",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "day_start_exposure_ms",
                table: "camera_pipeline_config",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "day_start_gain",
                table: "camera_pipeline_config",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "night_max_exposure_ms",
                table: "camera_pipeline_config",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "night_max_gain",
                table: "camera_pipeline_config",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "night_min_exposure_ms",
                table: "camera_pipeline_config",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "night_min_gain",
                table: "camera_pipeline_config",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "night_start_exposure_ms",
                table: "camera_pipeline_config",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "night_start_gain",
                table: "camera_pipeline_config",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "camera_pipeline_config",
                keyColumn: "id",
                keyValue: 1,
                columns: new[] { "day_max_exposure_ms", "day_max_gain", "day_min_exposure_ms", "day_min_gain", "day_start_exposure_ms", "day_start_gain", "night_max_exposure_ms", "night_max_gain", "night_min_exposure_ms", "night_min_gain", "night_start_exposure_ms", "night_start_gain" },
                values: new object[] { 60000, 500, 1, 0, 2000, 50, 60000, 500, 1, 0, 5000, 200 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "day_max_exposure_ms",
                table: "camera_pipeline_config");

            migrationBuilder.DropColumn(
                name: "day_max_gain",
                table: "camera_pipeline_config");

            migrationBuilder.DropColumn(
                name: "day_min_exposure_ms",
                table: "camera_pipeline_config");

            migrationBuilder.DropColumn(
                name: "day_min_gain",
                table: "camera_pipeline_config");

            migrationBuilder.DropColumn(
                name: "day_start_exposure_ms",
                table: "camera_pipeline_config");

            migrationBuilder.DropColumn(
                name: "day_start_gain",
                table: "camera_pipeline_config");

            migrationBuilder.DropColumn(
                name: "night_max_exposure_ms",
                table: "camera_pipeline_config");

            migrationBuilder.DropColumn(
                name: "night_max_gain",
                table: "camera_pipeline_config");

            migrationBuilder.DropColumn(
                name: "night_min_exposure_ms",
                table: "camera_pipeline_config");

            migrationBuilder.DropColumn(
                name: "night_min_gain",
                table: "camera_pipeline_config");

            migrationBuilder.DropColumn(
                name: "night_start_exposure_ms",
                table: "camera_pipeline_config");

            migrationBuilder.DropColumn(
                name: "night_start_gain",
                table: "camera_pipeline_config");
        }
    }
}
