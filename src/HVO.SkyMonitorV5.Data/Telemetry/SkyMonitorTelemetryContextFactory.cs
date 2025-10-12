using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HVO.SkyMonitorV5.Data.Telemetry;

/// <summary>
/// Design-time factory for generating migrations for <see cref="SkyMonitorTelemetryContext"/>.
/// </summary>
public sealed class SkyMonitorTelemetryContextFactory : IDesignTimeDbContextFactory<SkyMonitorTelemetryContext>
{
    public SkyMonitorTelemetryContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SkyMonitorTelemetryContext>();

        var basePath = AppContext.BaseDirectory;
        var databasePath = Path.Combine(basePath, "skymonitor-telemetry.design.db");
        var connectionString = FormattableString.Invariant($"Data Source={databasePath}");

        optionsBuilder.UseSqlite(connectionString, sqliteOptions =>
        {
            sqliteOptions.MigrationsAssembly(typeof(SkyMonitorTelemetryContext).Assembly.FullName);
        });

        return new SkyMonitorTelemetryContext(optionsBuilder.Options);
    }
}
