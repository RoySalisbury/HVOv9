using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HVO.SkyMonitorV5.Data.Migrations.Configuration
{
    /// <inheritdoc />
    public partial class InitialConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "camera_catalog_camera",
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
                name: "camera_catalog_lens",
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

            migrationBuilder.CreateTable(
                name: "camera_pipeline_config",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    enable_stacking = table.Column<bool>(type: "INTEGER", nullable: false),
                    enable_image_overlays = table.Column<bool>(type: "INTEGER", nullable: false),
                    capture_interval_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    stacking_frame_count = table.Column<int>(type: "INTEGER", nullable: false),
                    stacking_buffer_min_frames = table.Column<int>(type: "INTEGER", nullable: false),
                    stacking_buffer_integration_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                    day_exposure_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    night_exposure_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    day_gain = table.Column<int>(type: "INTEGER", nullable: false),
                    night_gain = table.Column<int>(type: "INTEGER", nullable: false),
                    day_night_transition_hour_offset = table.Column<int>(type: "INTEGER", nullable: false),
                    overlay_text_format = table.Column<string>(type: "TEXT", nullable: false),
                    processed_image_format = table.Column<string>(type: "TEXT", nullable: false),
                    processed_image_quality = table.Column<int>(type: "INTEGER", nullable: false),
                    bg_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    bg_queue_capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    bg_overflow_policy = table.Column<string>(type: "TEXT", nullable: false),
                    bg_compression_mode = table.Column<string>(type: "TEXT", nullable: false),
                    bg_restart_delay_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                    bg_adaptive_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    bg_adaptive_min_capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    bg_adaptive_max_capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    bg_adaptive_increase_step = table.Column<int>(type: "INTEGER", nullable: false),
                    bg_adaptive_decrease_step = table.Column<int>(type: "INTEGER", nullable: false),
                    bg_adaptive_scale_up_percent = table.Column<int>(type: "INTEGER", nullable: false),
                    bg_adaptive_scale_down_percent = table.Column<int>(type: "INTEGER", nullable: false),
                    bg_adaptive_evaluation_window_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                    bg_adaptive_cooldown_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                    pacing_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    pacing_elevated_delay_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    pacing_high_delay_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    pacing_critical_delay_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    pacing_rejection_penalty_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    pacing_rejection_penalty_duration_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                    pacing_ramp_up_step_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    pacing_ramp_down_step_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    pacing_max_delay_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    dispatch_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    dispatch_mode = table.Column<string>(type: "TEXT", nullable: false),
                    dispatch_s3_bucket = table.Column<string>(type: "TEXT", nullable: true),
                    dispatch_fanout_exchange = table.Column<string>(type: "TEXT", nullable: true),
                    dispatch_region = table.Column<string>(type: "TEXT", nullable: false),
                    cardinal_offset_x = table.Column<int>(type: "INTEGER", nullable: false),
                    cardinal_offset_y = table.Column<int>(type: "INTEGER", nullable: false),
                    cardinal_rotation_deg = table.Column<int>(type: "INTEGER", nullable: false),
                    cardinal_radius_offset_px = table.Column<int>(type: "INTEGER", nullable: false),
                    cardinal_label_north = table.Column<string>(type: "TEXT", nullable: false),
                    cardinal_label_south = table.Column<string>(type: "TEXT", nullable: false),
                    cardinal_label_east = table.Column<string>(type: "TEXT", nullable: false),
                    cardinal_label_west = table.Column<string>(type: "TEXT", nullable: false),
                    cardinal_swap_east_west = table.Column<bool>(type: "INTEGER", nullable: false),
                    cardinal_circle_color = table.Column<string>(type: "TEXT", nullable: false),
                    cardinal_circle_opacity = table.Column<int>(type: "INTEGER", nullable: false),
                    cardinal_circle_thickness = table.Column<int>(type: "INTEGER", nullable: false),
                    cardinal_circle_line_style = table.Column<string>(type: "TEXT", nullable: false),
                    cardinal_label_fill_opacity = table.Column<int>(type: "INTEGER", nullable: false),
                    cardinal_label_padding = table.Column<int>(type: "INTEGER", nullable: false),
                    cardinal_label_corner_radius = table.Column<int>(type: "INTEGER", nullable: false),
                    cardinal_label_font_size = table.Column<int>(type: "INTEGER", nullable: false),
                    mask_offset_x = table.Column<int>(type: "INTEGER", nullable: false),
                    mask_offset_y = table.Column<int>(type: "INTEGER", nullable: false),
                    mask_radius_offset_px = table.Column<int>(type: "INTEGER", nullable: false),
                    mask_color = table.Column<string>(type: "TEXT", nullable: false),
                    mask_opacity = table.Column<int>(type: "INTEGER", nullable: false),
                    constellation_line_thickness = table.Column<double>(type: "REAL", nullable: false),
                    constellation_line_opacity = table.Column<double>(type: "REAL", nullable: false),
                    constellation_line_color = table.Column<string>(type: "TEXT", nullable: false),
                    constellation_use_dashed_line = table.Column<bool>(type: "INTEGER", nullable: false),
                    celestial_label_font_size = table.Column<double>(type: "REAL", nullable: false),
                    celestial_star_label_color = table.Column<string>(type: "TEXT", nullable: false),
                    celestial_planet_label_color = table.Column<string>(type: "TEXT", nullable: false),
                    celestial_deep_sky_label_color = table.Column<string>(type: "TEXT", nullable: false),
                    celestial_star_ring_radius = table.Column<double>(type: "REAL", nullable: false),
                    celestial_planet_ring_radius = table.Column<double>(type: "REAL", nullable: false),
                    celestial_deep_sky_ring_radius = table.Column<double>(type: "REAL", nullable: false),
                    celestial_use_auto_star_selection = table.Column<bool>(type: "INTEGER", nullable: false),
                    celestial_auto_star_count = table.Column<int>(type: "INTEGER", nullable: false),
                    celestial_auto_star_magnitude_limit = table.Column<double>(type: "REAL", nullable: false),
                    celestial_annotate_planets = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_camera_pipeline_config", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "observatory_site",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    slug = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    latitude_degrees = table.Column<double>(type: "REAL", nullable: false),
                    longitude_degrees = table.Column<double>(type: "REAL", nullable: false),
                    time_zone_id = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_observatory_site", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "star_catalog_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    magnitude_limit = table.Column<double>(type: "REAL", nullable: false),
                    min_max_altitude_degrees = table.Column<double>(type: "REAL", nullable: false),
                    top_star_count = table.Column<int>(type: "INTEGER", nullable: false),
                    stratified_selection = table.Column<bool>(type: "INTEGER", nullable: false),
                    include_planets = table.Column<bool>(type: "INTEGER", nullable: false),
                    include_moon = table.Column<bool>(type: "INTEGER", nullable: false),
                    include_outer_planets = table.Column<bool>(type: "INTEGER", nullable: false),
                    include_sun = table.Column<bool>(type: "INTEGER", nullable: false),
                    right_ascension_bins = table.Column<int>(type: "INTEGER", nullable: false),
                    declination_bands = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_star_catalog_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rig_catalog_entry",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    camera_id = table.Column<int>(type: "INTEGER", nullable: false),
                    lens_id = table.Column<int>(type: "INTEGER", nullable: false),
                    boresight_alt_deg = table.Column<double>(type: "REAL", nullable: false),
                    boresight_az_deg = table.Column<double>(type: "REAL", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rig_catalog_entry", x => x.id);
                    table.ForeignKey(
                        name: "FK_rig_catalog_entry_camera_catalog_camera_camera_id",
                        column: x => x.camera_id,
                        principalTable: "camera_catalog_camera",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rig_catalog_entry_camera_catalog_lens_lens_id",
                        column: x => x.lens_id,
                        principalTable: "camera_catalog_lens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "camera_pipeline_filter",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    display_order = table.Column<int>(type: "INTEGER", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    pipeline_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_camera_pipeline_filter", x => x.id);
                    table.ForeignKey(
                        name: "FK_camera_pipeline_filter_camera_pipeline_config_pipeline_id",
                        column: x => x.pipeline_id,
                        principalTable: "camera_pipeline_config",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "celestial_annotation_deep_sky_object",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    right_ascension_hours = table.Column<double>(type: "REAL", nullable: false),
                    declination_degrees = table.Column<double>(type: "REAL", nullable: false),
                    magnitude = table.Column<double>(type: "REAL", nullable: false),
                    color = table.Column<string>(type: "TEXT", nullable: false),
                    pipeline_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_celestial_annotation_deep_sky_object", x => x.id);
                    table.ForeignKey(
                        name: "FK_celestial_annotation_deep_sky_object_camera_pipeline_config_pipeline_id",
                        column: x => x.pipeline_id,
                        principalTable: "camera_pipeline_config",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "camera_adapter_config",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    adapter_type = table.Column<string>(type: "TEXT", nullable: false),
                    rig_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_camera_adapter_config", x => x.id);
                    table.ForeignKey(
                        name: "FK_camera_adapter_config_rig_catalog_entry_rig_id",
                        column: x => x.rig_id,
                        principalTable: "rig_catalog_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "camera_catalog_camera",
                columns: new[] { "id", "adapter_name", "additional_tags_json", "body_type", "color_mode", "cooling", "display_name", "driver_id", "driver_version", "is_synthetic", "key", "manufacturer", "model", "pixel_size_microns", "sensor_cx_px", "sensor_cy_px", "sensor_height_px", "sensor_technology", "sensor_width_px", "supports_exposure_control", "supports_gain_control", "supports_software_binning", "supports_temperature_telemetry", "synthetic_profile" },
                values: new object[] { 1, "MockCameraAdapter", "[\"Simulation\"]", "Synthetic", "Monochrome", "None", "Mock ASI174MM", "Synthetic", "2.0.0", true, "MockASI174MM", "HVO", "Mock Fisheye AllSky", 5.8600000000000003, null, null, 1216, "Cmos", 1936, true, true, true, false, "MockFisheye" });

            migrationBuilder.InsertData(
                table: "camera_catalog_lens",
                columns: new[] { "id", "display_name", "fov_x_deg", "fov_y_deg", "focal_length_mm", "key", "kind", "projection_model", "roll_deg" },
                values: new object[] { 1, "Fujinon FE185C086HA-1", 185.0, 185.0, 2.7000000000000002, "Fujinon_FE185C086HA_1", "Fisheye", "Equidistant", 0.0 });

            migrationBuilder.InsertData(
                table: "camera_pipeline_config",
                columns: new[] { "id", "bg_adaptive_cooldown_seconds", "bg_adaptive_decrease_step", "bg_adaptive_enabled", "bg_adaptive_evaluation_window_seconds", "bg_adaptive_increase_step", "bg_adaptive_max_capacity", "bg_adaptive_min_capacity", "bg_adaptive_scale_down_percent", "bg_adaptive_scale_up_percent", "bg_compression_mode", "bg_enabled", "bg_overflow_policy", "bg_queue_capacity", "bg_restart_delay_seconds", "capture_interval_ms", "day_exposure_ms", "day_gain", "day_night_transition_hour_offset", "enable_image_overlays", "enable_stacking", "name", "night_exposure_ms", "night_gain", "overlay_text_format", "stacking_buffer_integration_seconds", "stacking_buffer_min_frames", "stacking_frame_count", "pacing_critical_delay_ms", "pacing_elevated_delay_ms", "pacing_enabled", "pacing_high_delay_ms", "pacing_max_delay_ms", "pacing_ramp_down_step_ms", "pacing_ramp_up_step_ms", "pacing_rejection_penalty_duration_seconds", "pacing_rejection_penalty_ms", "cardinal_circle_color", "cardinal_circle_line_style", "cardinal_circle_opacity", "cardinal_circle_thickness", "cardinal_label_corner_radius", "cardinal_label_east", "cardinal_label_fill_opacity", "cardinal_label_font_size", "cardinal_label_north", "cardinal_label_padding", "cardinal_label_south", "cardinal_label_west", "cardinal_offset_x", "cardinal_offset_y", "cardinal_radius_offset_px", "cardinal_rotation_deg", "cardinal_swap_east_west", "celestial_annotate_planets", "celestial_auto_star_count", "celestial_auto_star_magnitude_limit", "celestial_deep_sky_label_color", "celestial_deep_sky_ring_radius", "celestial_label_font_size", "celestial_planet_label_color", "celestial_planet_ring_radius", "celestial_star_label_color", "celestial_star_ring_radius", "celestial_use_auto_star_selection", "mask_color", "mask_opacity", "mask_offset_x", "mask_offset_y", "mask_radius_offset_px", "constellation_line_color", "constellation_line_opacity", "constellation_line_thickness", "constellation_use_dashed_line", "processed_image_format", "processed_image_quality", "dispatch_enabled", "dispatch_fanout_exchange", "dispatch_mode", "dispatch_region", "dispatch_s3_bucket" },
                values: new object[] { 1, 30, 4, true, 6, 4, 48, 24, 35, 75, "None", true, "Block", 32, 5, 1000, 1000, 30, 0, true, true, "Default", 25000, 200, "yyyy-MM-dd HH:mm:ss zzz", 120, 24, 4, 1000, 250, true, 500, 6000, 300, 150, 12, 2000, "#C8D2E6", "LongDash", 170, 1, 6, "E", 220, 18, "N", 6, "S", "W", 0, 0, -35, 0, true, true, 30, 3.0, "#F0E4FF", 12.0, 12.0, "#FFE8C5", 10.0, "#EBF5FF", 6.0, true, "#000000", 220, 0, 0, -4, "#7FB2FF", 0.40000000000000002, 0.80000000000000004, true, "Jpeg", 90, false, null, "None", "us-west-2", null });

            migrationBuilder.InsertData(
                table: "observatory_site",
                columns: new[] { "id", "latitude_degrees", "longitude_degrees", "name", "slug", "time_zone_id" },
                values: new object[] { 1, 35.347000000000001, -113.878, "Hualapai Valley Observatory", "hvo-primary", "America/Phoenix" });

            migrationBuilder.InsertData(
                table: "star_catalog_settings",
                columns: new[] { "id", "declination_bands", "include_moon", "include_outer_planets", "include_planets", "include_sun", "magnitude_limit", "min_max_altitude_degrees", "right_ascension_bins", "stratified_selection", "top_star_count" },
                values: new object[] { 1, 8, true, true, true, false, 6.5, 10.0, 24, false, 500 });

            migrationBuilder.InsertData(
                table: "camera_pipeline_filter",
                columns: new[] { "id", "display_order", "enabled", "name", "pipeline_id" },
                values: new object[,]
                {
                    { 1, 1, true, "CardinalDirections", 1 },
                    { 2, 2, true, "ConstellationFigures", 1 },
                    { 3, 3, true, "CelestialAnnotations", 1 },
                    { 4, 4, false, "OverlayText", 1 },
                    { 5, 5, true, "CircularApertureMask", 1 }
                });

            migrationBuilder.InsertData(
                table: "celestial_annotation_deep_sky_object",
                columns: new[] { "id", "color", "declination_degrees", "magnitude", "name", "right_ascension_hours", "pipeline_id" },
                values: new object[,]
                {
                    { 1, "#8FB7FF", 41.268999999999998, 3.3999999999999999, "M31 (Andromeda Galaxy)", 0.71199999999999997, 1 },
                    { 2, "#C6A7FF", 36.466999999999999, 5.7999999999999998, "M13 (Great Globular Cluster)", 16.695, 1 }
                });

            migrationBuilder.InsertData(
                table: "rig_catalog_entry",
                columns: new[] { "id", "boresight_alt_deg", "boresight_az_deg", "camera_id", "display_name", "is_active", "key", "lens_id" },
                values: new object[] { 1, 90.0, 0.0, 1, "Mock Fisheye", true, "MockFisheye", 1 });

            migrationBuilder.InsertData(
                table: "camera_adapter_config",
                columns: new[] { "id", "adapter_type", "name", "rig_id" },
                values: new object[] { 1, "Mock", "MockFisheye", 1 });

            migrationBuilder.CreateIndex(
                name: "IX_camera_adapter_config_name",
                table: "camera_adapter_config",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_camera_adapter_config_rig_id",
                table: "camera_adapter_config",
                column: "rig_id");

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

            migrationBuilder.CreateIndex(
                name: "IX_camera_pipeline_filter_pipeline_id",
                table: "camera_pipeline_filter",
                column: "pipeline_id");

            migrationBuilder.CreateIndex(
                name: "IX_celestial_annotation_deep_sky_object_pipeline_id",
                table: "celestial_annotation_deep_sky_object",
                column: "pipeline_id");

            migrationBuilder.CreateIndex(
                name: "IX_observatory_site_slug",
                table: "observatory_site",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rig_catalog_entry_camera_id",
                table: "rig_catalog_entry",
                column: "camera_id");

            migrationBuilder.CreateIndex(
                name: "IX_rig_catalog_entry_key",
                table: "rig_catalog_entry",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rig_catalog_entry_lens_id",
                table: "rig_catalog_entry",
                column: "lens_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "camera_adapter_config");

            migrationBuilder.DropTable(
                name: "camera_pipeline_filter");

            migrationBuilder.DropTable(
                name: "celestial_annotation_deep_sky_object");

            migrationBuilder.DropTable(
                name: "observatory_site");

            migrationBuilder.DropTable(
                name: "star_catalog_settings");

            migrationBuilder.DropTable(
                name: "rig_catalog_entry");

            migrationBuilder.DropTable(
                name: "camera_pipeline_config");

            migrationBuilder.DropTable(
                name: "camera_catalog_camera");

            migrationBuilder.DropTable(
                name: "camera_catalog_lens");
        }
    }
}
