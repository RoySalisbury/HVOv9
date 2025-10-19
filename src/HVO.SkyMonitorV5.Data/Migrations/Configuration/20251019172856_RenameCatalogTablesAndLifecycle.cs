using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.SkyMonitorV5.Data.Migrations.Configuration
{
    /// <inheritdoc />
    public partial class RenameCatalogTablesAndLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "camera_catalog_new",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    manufacturer = table.Column<string>(type: "TEXT", nullable: false),
                    model = table.Column<string>(type: "TEXT", nullable: false),
                    driver_version = table.Column<string>(type: "TEXT", nullable: false),
                    adapter_name = table.Column<string>(type: "TEXT", nullable: false),
                    driver_id = table.Column<string>(type: "TEXT", nullable: false),
                    is_synthetic = table.Column<bool>(type: "INTEGER", nullable: false),
                    synthetic_profile = table.Column<string>(type: "TEXT", nullable: true),
                    sensor_width_px = table.Column<int>(type: "INTEGER", nullable: false),
                    sensor_height_px = table.Column<int>(type: "INTEGER", nullable: false),
                    pixel_size_microns = table.Column<double>(type: "REAL", nullable: false),
                    sensor_cx_px = table.Column<double>(type: "REAL", nullable: true),
                    sensor_cy_px = table.Column<double>(type: "REAL", nullable: true),
                    color_mode = table.Column<string>(type: "TEXT", nullable: false),
                    sensor_technology = table.Column<string>(type: "TEXT", nullable: false),
                    body_type = table.Column<string>(type: "TEXT", nullable: false),
                    cooling = table.Column<string>(type: "TEXT", nullable: false),
                    supports_gain_control = table.Column<bool>(type: "INTEGER", nullable: false),
                    supports_exposure_control = table.Column<bool>(type: "INTEGER", nullable: false),
                    supports_temperature_telemetry = table.Column<bool>(type: "INTEGER", nullable: false),
                    supports_software_binning = table.Column<bool>(type: "INTEGER", nullable: false),
                    additional_tags_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    revision = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_camera_catalog", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "optics_catalog_new",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    projection_model = table.Column<string>(type: "TEXT", nullable: false),
                    focal_length_mm = table.Column<double>(type: "REAL", nullable: false),
                    fov_x_deg = table.Column<double>(type: "REAL", nullable: false),
                    fov_y_deg = table.Column<double>(type: "REAL", nullable: true),
                    roll_deg = table.Column<double>(type: "REAL", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    created_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    revision = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_optics_catalog", x => x.id);
                });

            migrationBuilder.Sql(
                "INSERT INTO camera_catalog_new (id, key, display_name, manufacturer, model, driver_version, adapter_name, driver_id, is_synthetic, synthetic_profile, sensor_width_px, sensor_height_px, pixel_size_microns, sensor_cx_px, sensor_cy_px, color_mode, sensor_technology, body_type, cooling, supports_gain_control, supports_exposure_control, supports_temperature_telemetry, supports_software_binning, additional_tags_json, created_utc, updated_utc, is_active, revision) " +
                "SELECT id, key, display_name, manufacturer, model, driver_version, adapter_name, driver_id, is_synthetic, synthetic_profile, sensor_width_px, sensor_height_px, pixel_size_microns, sensor_cx_px, sensor_cy_px, color_mode, sensor_technology, body_type, cooling, supports_gain_control, supports_exposure_control, supports_temperature_telemetry, supports_software_binning, additional_tags_json, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 1, 1 FROM camera_catalog_camera;");

            migrationBuilder.Sql(
                "INSERT INTO optics_catalog_new (id, key, display_name, projection_model, focal_length_mm, fov_x_deg, fov_y_deg, roll_deg, kind, created_utc, updated_utc, is_active, revision) " +
                "SELECT id, key, display_name, projection_model, focal_length_mm, fov_x_deg, fov_y_deg, roll_deg, kind, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 1, 1 FROM camera_catalog_lens;");

            migrationBuilder.DropForeignKey(
                name: "FK_rig_catalog_entry_camera_catalog_camera_camera_id",
                table: "rig_catalog_entry");

            migrationBuilder.DropForeignKey(
                name: "FK_rig_catalog_entry_camera_catalog_lens_lens_id",
                table: "rig_catalog_entry");

            migrationBuilder.Sql("PRAGMA foreign_keys = OFF;", suppressTransaction: true);

            migrationBuilder.DropTable(
                name: "camera_catalog_camera");

            migrationBuilder.DropTable(
                name: "camera_catalog_lens");

            migrationBuilder.RenameTable(
                name: "camera_catalog_new",
                newName: "camera_catalog");

            migrationBuilder.RenameTable(
                name: "optics_catalog_new",
                newName: "optics_catalog");

            migrationBuilder.CreateIndex(
                name: "IX_camera_catalog_key",
                table: "camera_catalog",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_optics_catalog_key",
                table: "optics_catalog",
                column: "key",
                unique: true);

            migrationBuilder.Sql("PRAGMA foreign_keys = ON;", suppressTransaction: true);

            migrationBuilder.AddForeignKey(
                name: "FK_rig_catalog_entry_camera_catalog_camera_id",
                table: "rig_catalog_entry",
                column: "camera_id",
                principalTable: "camera_catalog",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_rig_catalog_entry_optics_catalog_lens_id",
                table: "rig_catalog_entry",
                column: "lens_id",
                principalTable: "optics_catalog",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_rig_catalog_entry_camera_catalog_camera_id",
                table: "rig_catalog_entry");

            migrationBuilder.DropForeignKey(
                name: "FK_rig_catalog_entry_optics_catalog_lens_id",
                table: "rig_catalog_entry");

            migrationBuilder.Sql("PRAGMA foreign_keys = OFF;", suppressTransaction: true);

            migrationBuilder.CreateTable(
                name: "camera_catalog_camera_new",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    manufacturer = table.Column<string>(type: "TEXT", nullable: false),
                    model = table.Column<string>(type: "TEXT", nullable: false),
                    driver_version = table.Column<string>(type: "TEXT", nullable: false),
                    adapter_name = table.Column<string>(type: "TEXT", nullable: false),
                    driver_id = table.Column<string>(type: "TEXT", nullable: false),
                    is_synthetic = table.Column<bool>(type: "INTEGER", nullable: false),
                    synthetic_profile = table.Column<string>(type: "TEXT", nullable: true),
                    sensor_width_px = table.Column<int>(type: "INTEGER", nullable: false),
                    sensor_height_px = table.Column<int>(type: "INTEGER", nullable: false),
                    pixel_size_microns = table.Column<double>(type: "REAL", nullable: false),
                    sensor_cx_px = table.Column<double>(type: "REAL", nullable: true),
                    sensor_cy_px = table.Column<double>(type: "REAL", nullable: true),
                    color_mode = table.Column<string>(type: "TEXT", nullable: false),
                    sensor_technology = table.Column<string>(type: "TEXT", nullable: false),
                    body_type = table.Column<string>(type: "TEXT", nullable: false),
                    cooling = table.Column<string>(type: "TEXT", nullable: false),
                    supports_gain_control = table.Column<bool>(type: "INTEGER", nullable: false),
                    supports_exposure_control = table.Column<bool>(type: "INTEGER", nullable: false),
                    supports_temperature_telemetry = table.Column<bool>(type: "INTEGER", nullable: false),
                    supports_software_binning = table.Column<bool>(type: "INTEGER", nullable: false),
                    additional_tags_json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_camera_catalog_camera", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "camera_catalog_lens_new",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    projection_model = table.Column<string>(type: "TEXT", nullable: false),
                    focal_length_mm = table.Column<double>(type: "REAL", nullable: false),
                    fov_x_deg = table.Column<double>(type: "REAL", nullable: false),
                    fov_y_deg = table.Column<double>(type: "REAL", nullable: true),
                    roll_deg = table.Column<double>(type: "REAL", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_camera_catalog_lens", x => x.id);
                });

            migrationBuilder.Sql(
                "INSERT INTO camera_catalog_camera_new (id, key, display_name, manufacturer, model, driver_version, adapter_name, driver_id, is_synthetic, synthetic_profile, sensor_width_px, sensor_height_px, pixel_size_microns, sensor_cx_px, sensor_cy_px, color_mode, sensor_technology, body_type, cooling, supports_gain_control, supports_exposure_control, supports_temperature_telemetry, supports_software_binning, additional_tags_json) " +
                "SELECT id, key, display_name, manufacturer, model, driver_version, adapter_name, driver_id, is_synthetic, synthetic_profile, sensor_width_px, sensor_height_px, pixel_size_microns, sensor_cx_px, sensor_cy_px, color_mode, sensor_technology, body_type, cooling, supports_gain_control, supports_exposure_control, supports_temperature_telemetry, supports_software_binning, additional_tags_json FROM camera_catalog;");

            migrationBuilder.Sql(
                "INSERT INTO camera_catalog_lens_new (id, key, display_name, projection_model, focal_length_mm, fov_x_deg, fov_y_deg, roll_deg, kind) " +
                "SELECT id, key, display_name, projection_model, focal_length_mm, fov_x_deg, fov_y_deg, roll_deg, kind FROM optics_catalog;");

            migrationBuilder.DropTable(
                name: "camera_catalog");

            migrationBuilder.DropTable(
                name: "optics_catalog");

            migrationBuilder.Sql("PRAGMA foreign_keys = ON;", suppressTransaction: true);

            migrationBuilder.RenameTable(
                name: "camera_catalog_camera_new",
                newName: "camera_catalog_camera");

            migrationBuilder.RenameTable(
                name: "camera_catalog_lens_new",
                newName: "camera_catalog_lens");

            migrationBuilder.CreateIndex(
                name: "IX_camera_catalog_camera_key",
                table: "camera_catalog_camera",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_camera_catalog_lens_key",
                table: "camera_catalog_lens",
                column: "key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_rig_catalog_entry_camera_catalog_camera_camera_id",
                table: "rig_catalog_entry",
                column: "camera_id",
                principalTable: "camera_catalog_camera",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_rig_catalog_entry_camera_catalog_lens_lens_id",
                table: "rig_catalog_entry",
                column: "lens_id",
                principalTable: "camera_catalog_lens",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

