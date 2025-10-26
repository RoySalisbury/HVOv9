using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HVO.SkyMonitorV5.Data.Configurations;

/// <summary>
/// Provides design-time access to <see cref="SkyMonitorConfigurationContext"/> for Entity Framework Core tooling.
/// </summary>
public sealed class SkyMonitorConfigurationContextFactory : IDesignTimeDbContextFactory<SkyMonitorConfigurationContext>
{
    public SkyMonitorConfigurationContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SkyMonitorConfigurationContext>();

        var overridePath = Environment.GetEnvironmentVariable("SKYMONITOR_CONFIGURATION_DB_PATH");

        string databasePath;
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            databasePath = Path.GetFullPath(overridePath);
        }
        else
        {
            var baseDirectory = Directory.GetCurrentDirectory();
            databasePath = Path.Combine(baseDirectory, "SkyMonitor.Configuration.DesignTime.sqlite");
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        builder.UseSqlite(connectionString, options =>
        {
            options.MigrationsAssembly(typeof(SkyMonitorConfigurationContext).Assembly.FullName);
        });

        return new SkyMonitorConfigurationContext(builder.Options);
    }
}
