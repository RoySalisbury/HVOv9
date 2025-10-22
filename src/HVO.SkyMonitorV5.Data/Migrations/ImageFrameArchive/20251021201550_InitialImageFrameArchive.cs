using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.SkyMonitorV5.Data.Migrations.ImageFrameArchive
{
    /// <inheritdoc />
    public partial class InitialImageFrameArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "image_frame_archive",
                columns: table => new
                {
                    frame_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    rig_name = table.Column<string>(type: "TEXT", nullable: false),
                    camera_name = table.Column<string>(type: "TEXT", nullable: false),
                    frames_stacked = table.Column<int>(type: "INTEGER", nullable: false),
                    integration_ms = table.Column<int>(type: "INTEGER", nullable: true),
                    applied_filters_json = table.Column<string>(type: "TEXT", nullable: false),
                    queue_latency_ms = table.Column<double>(type: "REAL", nullable: true),
                    processing_ms = table.Column<double>(type: "REAL", nullable: true),
                    full_pipeline_ms = table.Column<double>(type: "REAL", nullable: true),
                    payload_content_type = table.Column<string>(type: "TEXT", nullable: false),
                    payload_extension = table.Column<string>(type: "TEXT", nullable: false),
                    thumbnail_file_path = table.Column<string>(type: "TEXT", nullable: true),
                    thumbnail_object_key = table.Column<string>(type: "TEXT", nullable: true),
                    thumbnail_bucket = table.Column<string>(type: "TEXT", nullable: true),
                    media_file_path = table.Column<string>(type: "TEXT", nullable: true),
                    media_object_key = table.Column<string>(type: "TEXT", nullable: true),
                    media_bucket = table.Column<string>(type: "TEXT", nullable: true),
                    raw_media_file_path = table.Column<string>(type: "TEXT", nullable: true),
                    raw_media_object_key = table.Column<string>(type: "TEXT", nullable: true),
                    raw_media_bucket = table.Column<string>(type: "TEXT", nullable: true),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_frame_archive", x => x.frame_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_image_frame_archive_camera_name",
                table: "image_frame_archive",
                column: "camera_name");

            migrationBuilder.CreateIndex(
                name: "ix_image_frame_archive_captured_at",
                table: "image_frame_archive",
                column: "captured_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_image_frame_archive_frames_stacked",
                table: "image_frame_archive",
                column: "frames_stacked");

            migrationBuilder.CreateIndex(
                name: "ix_image_frame_archive_rig_name",
                table: "image_frame_archive",
                column: "rig_name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "image_frame_archive");
        }
    }
}
