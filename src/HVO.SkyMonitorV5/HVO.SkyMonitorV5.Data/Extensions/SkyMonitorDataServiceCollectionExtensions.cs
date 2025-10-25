using System;
using HVO.SkyMonitorV5.Data.Abstractions;
using HVO.SkyMonitorV5.Data.Configurations;
using HVO.SkyMonitorV5.Data.Archive;
using HVO.SkyMonitorV5.Data.Options;
using HVO.SkyMonitorV5.Data.Services;
using HVO.SkyMonitorV5.Data.Telemetry;
using HVO.SkyMonitorV5.Data.Telemetry.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.Data.Extensions;

public static class SkyMonitorDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers shared SkyMonitor data-store infrastructure including options and path resolution services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">Optional configuration root used to bind <see cref="SkyMonitorDataRootOptions"/>.</param>
    /// <param name="configure">Optional callback for programmatic overrides.</param>
    /// <returns>The original service collection for chaining.</returns>
    public static IServiceCollection AddSkyMonitorDataInfrastructure(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<SkyMonitorDataRootOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<SkyMonitorDataRootOptions>();

        if (configuration is not null)
        {
            services.Configure<SkyMonitorDataRootOptions>(configuration.GetSection(SkyMonitorDataRootOptions.SectionName));
        }

        if (configure is not null)
        {
            services.PostConfigure(configure);
        }

        services.TryAddSingleton<ISkyMonitorDataPathProvider, SkyMonitorDataPathProvider>();

        return services;
    }

    /// <summary>
    /// Registers a SQLite-backed <see cref="DbContext"/> that stores its database under the configured data root.
    /// </summary>
    /// <typeparam name="TContext">The context type to register.</typeparam>
    /// <param name="services">The service collection to update.</param>
    /// <param name="relativePath">Relative file path (beneath the data root) for the SQLite database file.</param>
    /// <param name="configureSqlite">Optional callback for additional SQLite provider configuration.</param>
    /// <param name="configureOptions">Optional callback for additional <see cref="DbContextOptionsBuilder"/> configuration.</param>
    /// <param name="enableMigrations">When true, configures the context to locate its migrations assembly. Disable for read-only catalogs.</param>
    /// <returns>The original service collection for chaining.</returns>
    public static IServiceCollection AddSkyMonitorSqliteDbContext<TContext>(
        this IServiceCollection services,
        string relativePath,
        Action<SqliteDbContextOptionsBuilder>? configureSqlite = null,
        Action<DbContextOptionsBuilder>? configureOptions = null,
        SqliteOpenMode openMode = SqliteOpenMode.ReadWriteCreate,
        bool enableMigrations = true)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        services.AddDbContext<TContext>((provider, builder) =>
        {
            var pathProvider = provider.GetRequiredService<ISkyMonitorDataPathProvider>();
            var databasePath = pathProvider.ResolvePath(relativePath);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = openMode
            }.ToString();

            builder.UseSqlite(connectionString, sqliteOptions =>
            {
                if (enableMigrations)
                {
                    sqliteOptions.MigrationsAssembly(typeof(TContext).Assembly.FullName);
                }
                configureSqlite?.Invoke(sqliteOptions);
            });

            configureOptions?.Invoke(builder);
        }, contextLifetime: ServiceLifetime.Scoped, optionsLifetime: ServiceLifetime.Singleton);

        return services;
    }

    public static IServiceCollection AddSkyMonitorSqliteDbContextFactory<TContext>(
        this IServiceCollection services,
        string relativePath,
        Action<SqliteDbContextOptionsBuilder>? configureSqlite = null,
        Action<DbContextOptionsBuilder>? configureOptions = null,
        SqliteOpenMode openMode = SqliteOpenMode.ReadOnly,
        bool enableMigrations = true)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        services.AddDbContextFactory<TContext>((provider, builder) =>
        {
            var pathProvider = provider.GetRequiredService<ISkyMonitorDataPathProvider>();
            var databasePath = pathProvider.ResolvePath(relativePath);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = openMode
            }.ToString();

            builder.UseSqlite(connectionString, sqliteOptions =>
            {
                if (enableMigrations)
                {
                    sqliteOptions.MigrationsAssembly(typeof(TContext).Assembly.FullName);
                }
                configureSqlite?.Invoke(sqliteOptions);
            });

            builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            configureOptions?.Invoke(builder);
        });

        return services;
    }

    /// <summary>
    /// Registers the SkyMonitor configuration store with both scoped context access and factory support.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="relativePath">Relative path (beneath the data root) for the configuration database.</param>
    /// <param name="configureSqlite">Optional SQLite configuration callback.</param>
    /// <param name="configureOptions">Optional DbContext configuration callback.</param>
    /// <param name="openMode">SQLite open mode used when creating connections.</param>
    /// <returns>The original service collection for chaining.</returns>
    public static IServiceCollection AddSkyMonitorConfigurationStore(
        this IServiceCollection services,
        string relativePath = "configuration/sm-config.db",
        Action<SqliteDbContextOptionsBuilder>? configureSqlite = null,
        Action<DbContextOptionsBuilder>? configureOptions = null,
        SqliteOpenMode openMode = SqliteOpenMode.ReadWriteCreate)
    {
        services.AddSkyMonitorSqliteDbContext<SkyMonitorConfigurationContext>(
            relativePath,
            configureSqlite,
            configureOptions,
            openMode,
            enableMigrations: true);

        services.AddSkyMonitorSqliteDbContextFactory<SkyMonitorConfigurationContext>(
            relativePath,
            configureSqlite,
            configureOptions,
            openMode,
            enableMigrations: true);

        return services;
    }

    /// <summary>
    /// Registers the SkyMonitor telemetry store which captures diagnostic and performance telemetry.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="relativePath">Relative path (beneath the data root) for the telemetry database.</param>
    /// <param name="configureSqlite">Optional SQLite configuration callback.</param>
    /// <param name="configureOptions">Optional DbContext configuration callback.</param>
    /// <param name="openMode">SQLite open mode used when creating connections.</param>
    /// <returns>The original service collection for chaining.</returns>
    public static IServiceCollection AddSkyMonitorTelemetryStore(
        this IServiceCollection services,
        string relativePath = "telemetry/sm-telemetry.db",
        Action<SqliteDbContextOptionsBuilder>? configureSqlite = null,
        Action<DbContextOptionsBuilder>? configureOptions = null,
        SqliteOpenMode openMode = SqliteOpenMode.ReadWriteCreate)
    {
        services.AddSkyMonitorSqliteDbContext<SkyMonitorTelemetryContext>(
            relativePath,
            configureSqlite,
            configureOptions,
            openMode,
            enableMigrations: true);

        services.AddSkyMonitorSqliteDbContextFactory<SkyMonitorTelemetryContext>(
            relativePath,
            configureSqlite,
            configureOptions,
            openMode,
            enableMigrations: true);

        services.TryAddScoped<ISkyMonitorTelemetryRepository, SkyMonitorTelemetryRepository>();

        return services;
    }

    /// <summary>
    /// Registers the image frame archive store which backs the Image History experience.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="relativePath">Relative path (beneath the data root) for the archive database.</param>
    /// <param name="configureSqlite">Optional SQLite configuration callback.</param>
    /// <param name="configureOptions">Optional DbContext configuration callback.</param>
    /// <param name="openMode">SQLite open mode used when creating connections.</param>
    /// <returns>The original service collection for chaining.</returns>
    public static IServiceCollection AddSkyMonitorImageFrameArchive(
        this IServiceCollection services,
        string relativePath = "telemetry/image_frame_archive.sqlite",
        Action<SqliteDbContextOptionsBuilder>? configureSqlite = null,
        Action<DbContextOptionsBuilder>? configureOptions = null,
        SqliteOpenMode openMode = SqliteOpenMode.ReadWriteCreate)
    {
        services.AddSkyMonitorSqliteDbContext<ImageFrameArchiveContext>(
            relativePath,
            configureSqlite,
            configureOptions,
            openMode,
            enableMigrations: true);

        services.AddSkyMonitorSqliteDbContextFactory<ImageFrameArchiveContext>(
            relativePath,
            configureSqlite,
            configureOptions,
            openMode,
            enableMigrations: true);

        return services;
    }
}
