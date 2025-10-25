using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.SkyMonitorV5.Data.Migrations.Telemetry
{
    /// <inheritdoc />
    public partial class AddFullPipelineMillisecondsToFrameExport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "full_pipeline_ms",
                table: "frame_export_attempt",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "full_pipeline_ms",
                table: "frame_export_attempt");
        }
    }
}
