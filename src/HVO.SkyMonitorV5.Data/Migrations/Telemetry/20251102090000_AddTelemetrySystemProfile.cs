using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.SkyMonitorV5.Data.Migrations.Telemetry
{
    /// <inheritdoc />
    public partial class AddTelemetrySystemProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "telemetry_system_profile",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    system_hash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    machine_name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    host_name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    operating_system = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    os_architecture = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    process_architecture = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    framework_description = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    processor_count = table.Column<int>(type: "INTEGER", nullable: true),
                    total_memory_mb = table.Column<double>(type: "REAL", nullable: true),
                    cpu_model = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    hardware_model = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    is_containerized = table.Column<bool>(type: "INTEGER", nullable: true),
                    additional_properties_json = table.Column<string>(type: "TEXT", nullable: true),
                    first_seen_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry_system_profile", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_system_profile_last_seen",
                table: "telemetry_system_profile",
                column: "last_seen_at_utc");

            migrationBuilder.CreateIndex(
                name: "ux_telemetry_system_profile_hash",
                table: "telemetry_system_profile",
                column: "system_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telemetry_system_profile");
        }
    }
}
