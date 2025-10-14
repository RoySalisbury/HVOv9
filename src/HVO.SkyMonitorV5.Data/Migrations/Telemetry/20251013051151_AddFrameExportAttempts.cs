using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.SkyMonitorV5.Data.Migrations.Telemetry
{
    /// <inheritdoc />
    public partial class AddFrameExportAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "frame_export_attempt",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    attempted_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    attempted_at_local = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    frame_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    stage = table.Column<int>(type: "INTEGER", nullable: false),
                    sink_name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    success = table.Column<bool>(type: "INTEGER", nullable: false),
                    latency_ms = table.Column<double>(type: "REAL", nullable: true),
                    payload_bytes = table.Column<long>(type: "INTEGER", nullable: true),
                    payload_content_type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    payload_extension = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    queue_latency_ms = table.Column<double>(type: "REAL", nullable: true),
                    processing_ms = table.Column<double>(type: "REAL", nullable: true),
                    frames_stacked = table.Column<int>(type: "INTEGER", nullable: true),
                    integration_ms = table.Column<int>(type: "INTEGER", nullable: true),
                    error_message = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_frame_export_attempt", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_frame_export_attempt_frame",
                table: "frame_export_attempt",
                column: "frame_id");

            migrationBuilder.CreateIndex(
                name: "ix_frame_export_attempt_local",
                table: "frame_export_attempt",
                column: "attempted_at_local");

            migrationBuilder.CreateIndex(
                name: "ix_frame_export_attempt_stage_sink",
                table: "frame_export_attempt",
                columns: new[] { "stage", "sink_name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "frame_export_attempt");
        }
    }
}
