using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.SkyMonitorV5.Data.Migrations.Telemetry
{
    /// <inheritdoc />
    public partial class InitialTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "background_stacker_sample",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    captured_at_local = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    queue_fill_percentage = table.Column<double>(type: "REAL", nullable: false),
                    queue_depth = table.Column<int>(type: "INTEGER", nullable: false),
                    queue_capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    queue_latency_ms = table.Column<double>(type: "REAL", nullable: true),
                    stack_duration_ms = table.Column<double>(type: "REAL", nullable: true),
                    filter_duration_ms = table.Column<double>(type: "REAL", nullable: true),
                    queue_pressure_level = table.Column<int>(type: "INTEGER", nullable: false),
                    seconds_since_last_completed = table.Column<double>(type: "REAL", nullable: true),
                    queue_memory_mb = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_background_stacker_sample", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "capture_pacing_sample",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    captured_at_local = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    using_background_stacker = table.Column<bool>(type: "INTEGER", nullable: false),
                    base_delay_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    adjusted_delay_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    queue_pressure_level = table.Column<int>(type: "INTEGER", nullable: false),
                    pressure_delay_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    penalty_delay_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    penalty_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    penalty_expires_at_local = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capture_pacing_sample", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "filter_metric_sample",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    captured_at_local = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    filter_name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    applied_count = table.Column<long>(type: "INTEGER", nullable: false),
                    last_duration_ms = table.Column<double>(type: "REAL", nullable: true),
                    average_duration_ms = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_filter_metric_sample", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processing_queue_sample",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    captured_at_local = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    depth = table.Column<int>(type: "INTEGER", nullable: false),
                    backpressure_events = table.Column<int>(type: "INTEGER", nullable: false),
                    last_enqueue_wait_ms = table.Column<double>(type: "REAL", nullable: false),
                    peak_enqueue_wait_ms = table.Column<double>(type: "REAL", nullable: false),
                    avg_enqueue_wait_ms = table.Column<double>(type: "REAL", nullable: false),
                    last_processing_ms = table.Column<double>(type: "REAL", nullable: false),
                    peak_processing_ms = table.Column<double>(type: "REAL", nullable: false),
                    avg_processing_ms = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_queue_sample", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "remote_dispatch_attempt",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    attempted_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    attempted_at_local = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    mode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    latency_ms = table.Column<double>(type: "REAL", nullable: true),
                    payload_bytes = table.Column<long>(type: "INTEGER", nullable: true),
                    payload_content_type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    payload_extension = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    message = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    error_message = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    format_key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remote_dispatch_attempt", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "telemetry_event",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    occurred_at_local = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    category = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    event_type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    severity = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    summary = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    detail = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    properties_json = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry_event", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_background_stacker_sample_local",
                table: "background_stacker_sample",
                column: "captured_at_local");

            migrationBuilder.CreateIndex(
                name: "ix_capture_pacing_sample_local",
                table: "capture_pacing_sample",
                column: "captured_at_local");

            migrationBuilder.CreateIndex(
                name: "ix_filter_metric_sample_filter_time",
                table: "filter_metric_sample",
                columns: new[] { "filter_name", "captured_at_local" });

            migrationBuilder.CreateIndex(
                name: "ix_processing_queue_sample_local",
                table: "processing_queue_sample",
                column: "captured_at_local");

            migrationBuilder.CreateIndex(
                name: "ix_remote_dispatch_attempt_format",
                table: "remote_dispatch_attempt",
                column: "format_key");

            migrationBuilder.CreateIndex(
                name: "ix_remote_dispatch_attempt_local",
                table: "remote_dispatch_attempt",
                column: "attempted_at_local");

            migrationBuilder.CreateIndex(
                name: "ix_remote_dispatch_attempt_outcome",
                table: "remote_dispatch_attempt",
                column: "outcome");

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_event_category",
                table: "telemetry_event",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_event_local",
                table: "telemetry_event",
                column: "occurred_at_local");

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_event_type",
                table: "telemetry_event",
                column: "event_type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "background_stacker_sample");

            migrationBuilder.DropTable(
                name: "capture_pacing_sample");

            migrationBuilder.DropTable(
                name: "filter_metric_sample");

            migrationBuilder.DropTable(
                name: "processing_queue_sample");

            migrationBuilder.DropTable(
                name: "remote_dispatch_attempt");

            migrationBuilder.DropTable(
                name: "telemetry_event");
        }
    }
}
