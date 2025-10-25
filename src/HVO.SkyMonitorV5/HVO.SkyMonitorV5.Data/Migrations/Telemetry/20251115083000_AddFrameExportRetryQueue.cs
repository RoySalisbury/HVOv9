using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.SkyMonitorV5.Data.Migrations.Telemetry
{
    /// <inheritdoc />
    public partial class AddFrameExportRetryQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "frame_export_retry",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    frame_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    stage = table.Column<int>(type: "INTEGER", nullable: false),
                    sink_name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    enqueued_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    last_attempt_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    attempt_count = table.Column<int>(type: "INTEGER", nullable: false),
                    payload = table.Column<byte[]>(type: "BLOB", nullable: false),
                    content_type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    file_extension = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    metadata_json = table.Column<string>(type: "TEXT", nullable: false),
                    last_error_message = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_frame_export_retry", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_frame_export_retry_next_attempt",
                table: "frame_export_retry",
                column: "next_attempt_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_frame_export_retry_stage_sink",
                table: "frame_export_retry",
                columns: new[] { "stage", "sink_name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "frame_export_retry");
        }
    }
}
