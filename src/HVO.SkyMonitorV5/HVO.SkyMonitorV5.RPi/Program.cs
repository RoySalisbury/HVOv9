using Asp.Versioning;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using HVO.SkyMonitorV5.Data.Catalogs.Constellations;
using HVO.SkyMonitorV5.Data.Catalogs.DeepSky;
using HVO.SkyMonitorV5.Data.Catalogs.Hyg;
using HVO.SkyMonitorV5.Data.Configurations;
using HVO.SkyMonitorV5.Data.Extensions;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.RPi.Components;
using HVO.SkyMonitorV5.RPi.Data;
using HVO.SkyMonitorV5.RPi.HostedServices;
using HVO.SkyMonitorV5.RPi.Middleware;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Composition;
using HVO.SkyMonitorV5.RPi.Pipeline.Preprocessing;
using HVO.SkyMonitorV5.RPi.Pipeline.Preprocessing.Calibration;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using HVO.SkyMonitorV5.RPi.Pipeline.Overlays;
using HVO.SkyMonitorV5.RPi.Catalog;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Zwo;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.ImageHistory;
using HVO.SkyMonitorV5.RPi.Storage;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Infrastructure.NativeMemory;
using HVO.SkyMonitorV5.RPi.Infrastructure.Resilience;
using HVO.SkyMonitorV5.RPi.Telemetry;
using HVO.SkyMonitorV5.RPi.Infrastructure.Logging;
using HVO.SkyMonitorV5.RPi.Infrastructure.HealthChecks;
using HVO.SkyMonitorV5.Data.Options;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Exports.Sinks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using System.Runtime.ExceptionServices;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Text;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace HVO.SkyMonitorV5.RPi;

/// <summary>
/// Application entry point for the SkyMonitor v5 Raspberry Pi host.
/// </summary>
public static class Program
{
    private static readonly ConcurrentDictionary<string, byte> FirstChanceExceptionCallsites = new();

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureServices(builder.Services, builder.Configuration);
        ConfigureLogging(builder.Logging);

        var app = builder.Build();
        ConfigureUnhandledExceptionLogging(app);
        Configure(app);

        app.Run();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSkyMonitorDataInfrastructure(configuration);

        var dataRootOptions = new SkyMonitorDataRootOptions();
        configuration.GetSection(SkyMonitorDataRootOptions.SectionName).Bind(dataRootOptions);
        var dataProtectionDirectory = Path.Combine(dataRootOptions.ResolveRootPath(), "dataprotection", "keys");
        Directory.CreateDirectory(dataProtectionDirectory);

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionDirectory))
            .SetApplicationName("HVO.SkyMonitorV5.RPi");

        services.AddSkyMonitorConfigurationStore(
            relativePath: "configuration/sm-config.db",
            configureOptions: builder => builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        services.AddSingleton<IDataStoreBootstrapStatus, DataStoreBootstrapStatus>();

        services.AddHostedService<ConfigurationStoreBootstrapper>();

        services.AddSkyMonitorTelemetryStore(relativePath: "telemetry/sm-telemetry.db");
        // Image Frame Archive - requires WAL mode and busy timeout for concurrent access
        services.AddSkyMonitorImageFrameArchive(
            relativePath: "telemetry/image_frame_archive.sqlite",
            configureSqlite: sqlite =>
            {
                // Enable WAL mode for better concurrent access
                sqlite.CommandTimeout(30);
            },
            configureOptions: options =>
            {
                // Add connection string modifications for WAL mode and busy timeout
                options.ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.AmbientTransactionWarning));
            });
        services.AddHostedService<ImageFrameArchiveBootstrapper>();
        services.AddHostedService<TelemetryStoreBootstrapper>();

        services.AddOptions<SkyMonitorTelemetryRetentionOptions>()
            .Bind(configuration.GetSection(SkyMonitorTelemetryRetentionOptions.SectionName))
            .ValidateDataAnnotations()
            .PostConfigure(options =>
            {
                options.RemoteDispatch ??= TelemetryRetentionPolicy.Create(TimeSpan.FromDays(30), 5_000);
                options.BackgroundStacker ??= TelemetryRetentionPolicy.Create(TimeSpan.FromDays(14), 15_000);
                options.CapturePacing ??= TelemetryRetentionPolicy.Create(TimeSpan.FromDays(14), 15_000);
                options.ProcessingQueue ??= TelemetryRetentionPolicy.Create(TimeSpan.FromDays(14), 15_000);
                options.FilterMetrics ??= TelemetryRetentionPolicy.Create(TimeSpan.FromDays(30), 5_000);
                options.TelemetryEvents ??= TelemetryRetentionPolicy.Create(TimeSpan.FromDays(30), 20_000);
            });

        services.AddSkyMonitorSqliteDbContext<HygContext>(
            relativePath: "catalogs/hyg_v42.sqlite",
            configureOptions: builder => builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking),
            openMode: SqliteOpenMode.ReadOnly,
            enableMigrations: false);

        services.AddSkyMonitorSqliteDbContextFactory<ConstellationCatalogContext>(
            relativePath: "catalogs/ConstellationLines.sqlite",
            openMode: SqliteOpenMode.ReadOnly,
            enableMigrations: false);

        services.AddSkyMonitorSqliteDbContextFactory<DeepSkyCatalogContext>(
            relativePath: "catalogs/deep-sky.sqlite",
            openMode: SqliteOpenMode.ReadOnly,
            enableMigrations: false);

        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 256;
        });

        services.AddSingleton<ICelestialProjector, CelestialProjector>();

        services.AddScoped<SkyMonitorRepository>();
        services.AddScoped<IStarRepository>(sp => sp.GetRequiredService<SkyMonitorRepository>());
        services.AddScoped<IPlanetRepository>(sp => sp.GetRequiredService<SkyMonitorRepository>());
        services.AddScoped<IConstellationCatalog>(sp => sp.GetRequiredService<SkyMonitorRepository>());
        services.AddScoped<IDeepSkyCatalog>(sp => sp.GetRequiredService<SkyMonitorRepository>());

        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

        services.AddHttpClient();
        services.AddHttpContextAccessor();

        services.AddOptions<LocalApiClientOptions>()
            .Bind(configuration.GetSection(LocalApiClientOptions.SectionName))
            .PostConfigure(options =>
            {
                if (options.Timeout <= TimeSpan.Zero)
                {
                    options.Timeout = TimeSpan.FromSeconds(10);
                }
            });

        services.AddOptions<ImageHistoryOptions>()
            .Bind(configuration.GetSection(ImageHistoryOptions.SectionName))
            .ValidateDataAnnotations()
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ThumbnailsRelativePath))
                {
                    options.ThumbnailsRelativePath = "telemetry/image-history/thumbnails";
                }

                if (options.ThumbnailMaxAxisPixels <= 0)
                {
                    options.ThumbnailMaxAxisPixels = 320;
                }

                if (options.ThumbnailQuality <= 0)
                {
                    options.ThumbnailQuality = 86;
                }
            })
            .ValidateOnStart();

        services.AddHttpClient<ILocalApiClient, LocalApiClient>((sp, client) =>
        {
            // Prefer configuration for base address; switch to IHttpContextAccessor-based per-request inference if future routes demand host negotiation.
            var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<LocalApiClientOptions>>();
            var options = optionsMonitor.CurrentValue ?? new LocalApiClientOptions();

            if (!string.IsNullOrWhiteSpace(options.BaseAddress) && Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out var configuredBase))
            {
                client.BaseAddress = configuredBase;
            }
            else if (client.BaseAddress is null)
            {
                client.BaseAddress = new Uri("http://127.0.0.1:5136/", UriKind.Absolute);
            }

            client.Timeout = options.Timeout > TimeSpan.Zero ? options.Timeout : TimeSpan.FromSeconds(10);
        });

        services.AddExceptionHandler<HvoServiceExceptionHandler>();

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            var clock = context.HttpContext.RequestServices.GetService<IObservatoryClock>();
            var timestamp = clock?.LocalNow ?? DateTimeOffset.Now;
            context.ProblemDetails.Extensions["timestamp"] = timestamp;

            var activity = context.HttpContext.Features.Get<IHttpActivityFeature>()?.Activity;
            if (activity is not null)
            {
                context.ProblemDetails.Extensions["activityId"] = activity.Id;
            }

            if (context.HttpContext.Request.Headers.TryGetValue("User-Agent", out var userAgent))
            {
                context.ProblemDetails.Extensions["userAgent"] = userAgent.ToString();
            }
        });

        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("SkyMonitor v5 is running"))
            .AddCheck<S3FrameExportHealthCheck>("s3_export", tags: new[] { "s3", "readiness" });

        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "HVO SkyMonitor v5 API",
                    Version = "v1.0",
                    Description = "Camera capture and processing pipeline for the Hualapai Valley Observatory SkyMonitor v5 system",
                    Contact = new OpenApiContact
                    {
                        Name = "Administrator",
                        Email = "admin@hualapaivalleyobservatory.org"
                    }
                };
                return Task.CompletedTask;
            });
        });

        services.AddEndpointsApiExplorer();

        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        }).AddApiExplorer();

        services.AddOptions<CameraPipelineOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ObservatoryLocationOptions>()
            .ValidateDataAnnotations()
            .Validate(static options =>
                !double.IsNaN(options.LatitudeDegrees) && !double.IsNaN(options.LongitudeDegrees),
                "Observatory location must include both latitude and longitude values.")
            .ValidateOnStart();

        services.AddOptions<StarCatalogOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CardinalDirectionsOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CircularApertureMaskOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CelestialAnnotationsOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ConstellationFigureOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DiagnosticsOverlayOptions>()
            .Bind(configuration.GetSection(DiagnosticsOverlayOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<DatabaseBackedConfigurationOptionsConfigurator>();
        services.AddSingleton<IConfigurationSnapshotInvalidator>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<ObservatoryLocationOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<AllSkyCatalogOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<CameraPipelineOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<CardinalDirectionsOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<CircularApertureMaskOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<CelestialAnnotationsOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<ConstellationFigureOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<StarCatalogOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<LocalApiClientOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<SkyMonitorTelemetryRetentionOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<FrameExportOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());

        services.AddSingleton<ISystemConfigurationService, SystemConfigurationService>();
        services.AddSingleton<IRigRuntimeUpdater, RigRuntimeUpdater>();
        services.AddSingleton<IEquipmentConfigurationService, EquipmentConfigurationService>();

        services.AddSingleton<ISkyMonitorTelemetryIngestionQueue, SkyMonitorTelemetryIngestionQueue>();
        services.AddSingleton<SkyMonitorTelemetryMetrics>();
        services.AddSingleton<ISkyMonitorTelemetryRecorder, SkyMonitorTelemetryRecorder>();
        services.AddSingleton<ITelemetrySystemProfileCollector, TelemetrySystemProfileCollector>();
        services.AddScoped<ITelemetrySystemProfileRegistrar, TelemetrySystemProfileRegistrar>();
        services.AddHostedService<SkyMonitorTelemetryIngestionService>();
        services.AddSingleton<SkyMonitorTelemetryRetentionProcessor>();
        services.AddHostedService<SkyMonitorTelemetryRetentionService>();

        services.AddSingleton<IFrameStateStore, FrameStateStore>();
        services.AddScoped<IFrameMediaProvider, FrameMediaProvider>();
        services.AddScoped<IImageHistoryService, ImageHistoryService>();

        services.AddSingleton<IExposureAnalyzer, SimpleExposureAnalyzer>();
        services.AddSingleton<IExposureController, AdaptiveExposureController>();
        services.AddSingleton<SkiaSurfacePool>();
        services.AddSingleton<INativeBufferLeaseFactory>(HGlobalNativeBufferLeaseFactory.Shared);
        services.AddSingleton<IFrameCalibrationPipelineFactory>(NullFrameCalibrationPipelineFactory.Instance);
        services.AddSingleton<IFramePreprocessingOrchestrator, FramePreprocessingOrchestrator>();
        services.AddSingleton<OverlayAssetCache>();
        services.AddSingleton<FrameComposer>();
        services.AddSingleton<IFrameStacker, RollingFrameStacker>();
        services.AddSingleton<IMinioClientProvider, MinioClientProvider>();
        services.AddSingleton<IRemoteFrameEncoder, SkiaRemoteFrameEncoder>();
        services.AddSingleton<IRemoteFramePublisher, RemoteFramePublisher>();

        services.AddOptions<FrameExportOptions>()
            .Bind(configuration.GetSection(FrameExportOptions.SectionName))
            .ValidateDataAnnotations()
            .PostConfigure(options => options.Normalize())
            .ValidateOnStart();

        services.AddSingleton<IPostConfigureOptions<FrameExportOptions>, ImageHistoryFrameExportOptionsConfigurator>();

        services.AddOptions<FrameExportResilienceOptions>()
            .Bind(configuration.GetSection(FrameExportResilienceOptions.SectionName))
            .ValidateDataAnnotations()
            .PostConfigure(options => options.Normalize());

        services.AddOptions<FrameExportRetryOptions>()
            .Bind(configuration.GetSection(FrameExportRetryOptions.SectionName))
            .ValidateDataAnnotations()
            .PostConfigure(options => options.Normalize());

        services.AddSingleton<IFitsFrameEncoder, FitsFrameEncoder>();

        services.AddOptions<SkiaPipelineFeatureOptions>()
            .Bind(configuration.GetSection(SkiaPipelineFeatureOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IFrameExportResiliencePolicyProvider, FrameExportResiliencePolicyProvider>();

        services.AddSingleton<IFrameExportSink>(sp =>
            new FilesystemFrameExportSink(
                FrameExportStage.Raw,
                sp.GetRequiredService<IOptionsMonitor<FrameExportOptions>>(),
                sp.GetRequiredService<ILogger<FilesystemFrameExportSink>>()));

        services.AddSingleton<IFrameExportSink>(sp =>
            new FilesystemFrameExportSink(
                FrameExportStage.Processed,
                sp.GetRequiredService<IOptionsMonitor<FrameExportOptions>>(),
                sp.GetRequiredService<ILogger<FilesystemFrameExportSink>>()));

        services.AddSingleton<IFrameExportSink>(sp =>
            new S3FrameExportSink(
                FrameExportStage.Raw,
                sp.GetRequiredService<IOptionsMonitor<FrameExportOptions>>(),
                sp.GetRequiredService<IMinioClientProvider>(),
                sp.GetRequiredService<IFrameExportResiliencePolicyProvider>(),
                sp.GetRequiredService<HealthCheckService>(),
                sp.GetRequiredService<ILogger<S3FrameExportSink>>()));

        services.AddSingleton<IFrameExportSink>(sp =>
            new S3FrameExportSink(
                FrameExportStage.Processed,
                sp.GetRequiredService<IOptionsMonitor<FrameExportOptions>>(),
                sp.GetRequiredService<IMinioClientProvider>(),
                sp.GetRequiredService<IFrameExportResiliencePolicyProvider>(),
                sp.GetRequiredService<HealthCheckService>(),
                sp.GetRequiredService<ILogger<S3FrameExportSink>>()));

        services.AddOptions<FrameExportDispatcherOptions>()
            .Bind(configuration.GetSection(FrameExportDispatcherOptions.SectionName))
            .PostConfigure(options =>
            {
                options.ChannelCapacity = Math.Max(1, options.ChannelCapacity);
                options.MaxConcurrency = Math.Max(1, options.MaxConcurrency);
                if (options.DrainTimeout <= TimeSpan.Zero)
                {
                    options.DrainTimeout = TimeSpan.FromSeconds(30);
                }
            });

        services.AddSingleton<FrameExportMetrics>();
        services.AddSingleton<FrameExportRetryService>();
        services.AddSingleton<IFrameExportRetryQueue>(sp => sp.GetRequiredService<FrameExportRetryService>());
        services.AddHostedService(sp => sp.GetRequiredService<FrameExportRetryService>());
        services.AddSingleton<FrameExportDispatcher>();
        services.AddSingleton<IFrameExportDispatcher>(sp => sp.GetRequiredService<FrameExportDispatcher>());
        services.AddHostedService(sp => sp.GetRequiredService<FrameExportDispatcher>());
        services.AddSingleton<IProcessedFrameEncoder, ProcessedFrameEncoder>();
        services.AddSingleton<ISkiaPipelineFeatureToggleMonitor, SkiaPipelineFeatureToggleMonitor>();

        services.AddSingleton<ImageFrameArchiveIngestionService>();
        services.AddSingleton<IImageFrameArchiveIngestionQueue>(sp => sp.GetRequiredService<ImageFrameArchiveIngestionService>());
        services.AddHostedService(sp => sp.GetRequiredService<ImageFrameArchiveIngestionService>());
        services.AddSingleton<FrameExportPublisher>();
        services.AddSingleton<BackgroundFrameStackerService>();
        services.AddSingleton<IBackgroundFrameStacker>(sp => sp.GetRequiredService<BackgroundFrameStackerService>());
        services.AddHostedService(sp => sp.GetRequiredService<BackgroundFrameStackerService>());
        services.AddSingleton<RemoteDispatchMetricsObserver>();
        services.AddHostedService(sp => sp.GetRequiredService<RemoteDispatchMetricsObserver>());

        services.AddSingleton<IFrameFilter, CardinalDirectionsFilter>();
        services.AddSingleton<IFrameFilter, ConstellationFigureFilter>();
        services.AddSingleton<IFrameFilter, CelestialAnnotationsFilter>();
        services.AddSingleton<IFrameFilter, OverlayTextFilter>();
        services.AddSingleton<IFrameFilter, CircularApertureMaskFilter>();
        services.AddSingleton<IFrameFilter, DiagnosticsOverlayFilter>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IObservatoryClock, ObservatoryClock>();

        services.AddOptions<AllSkyCatalogOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<AllSkyCatalogRegistry>();
        services.AddSingleton<ICameraCatalog>(sp => sp.GetRequiredService<AllSkyCatalogRegistry>());
        services.AddSingleton<ILensCatalog>(sp => sp.GetRequiredService<AllSkyCatalogRegistry>());
        services.AddSingleton<IRigCatalog>(sp => sp.GetRequiredService<AllSkyCatalogRegistry>());
        services.AddHostedService<CatalogConfigurationReporter>();

        services.AddSingleton<ICameraDriverRegistry, CameraDriverRegistry>();
        services.AddSingleton<ICameraDriverFactory, CameraDriverFactory>();
        services.AddSingleton<IRigAcquisitionAdapter, RigAcquisitionAdapter>();

        services.AddSingleton<FrameFilterPipeline>(sp =>
        {
            var filters = sp.GetServices<IFrameFilter>();
            var composer = sp.GetRequiredService<FrameComposer>();
            var logger = sp.GetRequiredService<ILogger<FrameFilterPipeline>>();
            var telemetry = sp.GetService<ISkyMonitorTelemetryRecorder>();
            return new FrameFilterPipeline(filters, composer, logger, telemetry);
        });
        services.AddSingleton<IFrameFilterPipeline>(sp => sp.GetRequiredService<FrameFilterPipeline>());
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();

        RegisterCameraAdapters(services);

        services.AddHostedService<AllSkyCaptureService>();

        services.AddOpenTelemetry()
            .WithMetrics(builder =>
            {
                builder.ConfigureResource(resourceBuilder => resourceBuilder.AddService(
                    serviceName: "HVO.SkyMonitorV5.RPi",
                    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"));

                builder.AddMeter("HVO.SkyMonitor.BackgroundStacker");
                builder.AddMeter("HVO.SkyMonitor.RemoteDispatch");
                builder.AddMeter("HVO.SkyMonitor.Telemetry");
                builder.AddMeter("HVO.SkyMonitor.FrameExport");
                builder.AddPrometheusExporter();
            });
    }

    private static void ConfigureUnhandledExceptionLogging(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("HVO.SkyMonitorV5.UnhandledExceptions");
        var configuration = app.Configuration;
        var logFirstChance = configuration.GetValue<bool?>("SkyMonitor:Diagnostics:LogFirstChanceExceptions") ?? false;

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                logger.LogCritical(exception, "Unhandled exception detected. Terminating: {IsTerminating}", eventArgs.IsTerminating);
            }
            else
            {
                logger.LogCritical("Unhandled non-exception error detected. Terminating: {IsTerminating}. Payload: {Payload}", eventArgs.IsTerminating, eventArgs.ExceptionObject);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            logger.LogError(eventArgs.Exception, "Unobserved task exception captured by scheduler.");
            eventArgs.SetObserved();
        };

        if (logFirstChance)
        {
            AppDomain.CurrentDomain.FirstChanceException += (_, eventArgs) =>
            {
                var exception = eventArgs.Exception;
                if (exception is OperationCanceledException or ChannelClosedException)
                {
                    var callSite = TryGetFirstChanceCallSiteSignature();

                    if (callSite != null && FirstChanceExceptionCallsites.Count >= 512)
                    {
                        FirstChanceExceptionCallsites.Clear();
                    }

                    var isNewCallSite = callSite != null && FirstChanceExceptionCallsites.TryAdd(callSite, 0);
                    var logLevel = isNewCallSite ? LogLevel.Debug : (callSite is null ? LogLevel.Debug : LogLevel.Trace);

                    logger.Log(logLevel,
                        exception,
                        "First-chance {ExceptionType} observed on thread {ThreadId}{CallSiteInformation}. (newCallSite={IsNewCallSite})",
                        exception.GetType().FullName,
                        Environment.CurrentManagedThreadId,
                        callSite is null ? string.Empty : $" at {callSite}",
                        isNewCallSite);
                }
            };
        }
    }

    private static string? TryGetFirstChanceCallSiteSignature()
    {
        var stackTrace = new StackTrace(skipFrames: 2, fNeedFileInfo: false);
        var frames = stackTrace.GetFrames();

        if (frames is null || frames.Length == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        var appended = 0;

        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            if (method is null)
            {
                continue;
            }

            var declaringType = method.DeclaringType;
            if (declaringType is null)
            {
                continue;
            }

            var @namespace = declaringType.Namespace ?? string.Empty;
            if (@namespace.StartsWith("System.", StringComparison.Ordinal) && !@namespace.StartsWith("HVO.", StringComparison.Ordinal))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(" > ");
            }

            builder.Append(declaringType.FullName);
            builder.Append('.');
            builder.Append(method.Name);

            appended++;
            if (appended >= 4)
            {
                break;
            }
        }

        return appended == 0 ? null : builder.ToString();
    }

    private static void RegisterCameraAdapters(IServiceCollection services)
    {
        services.AddSingleton<ICameraAdapter>(sp =>
        {
            var configurator = sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>();
            var adapters = configurator.GetCameraAdapters();

            if (adapters.Count == 0)
            {
                throw new InvalidOperationException("No all-sky camera adapters are configured in the SkyMonitor data store.");
            }

            var catalogOptions = sp.GetRequiredService<IOptionsMonitor<AllSkyCatalogOptions>>().CurrentValue;
            var preferredRigKey = catalogOptions?.Rigs?.ActiveRig;

            var descriptor = adapters
                .FirstOrDefault(adapter =>
                    !string.IsNullOrWhiteSpace(preferredRigKey) &&
                    string.Equals(adapter.RigKey, preferredRigKey, StringComparison.OrdinalIgnoreCase))
                ?? adapters[0];

            if (string.IsNullOrWhiteSpace(descriptor.RigKey))
            {
                throw new InvalidOperationException($"Camera adapter '{descriptor.Name}' must reference a rig catalog entry.");
            }

            var adapterOptions = new CameraAdapterOptions
            {
                Name = descriptor.Name,
                Adapter = descriptor.AdapterType,
                RigCatalog = descriptor.RigKey
            };

            Validator.ValidateObject(adapterOptions, new ValidationContext(adapterOptions), validateAllProperties: true);

            var rigCatalog = sp.GetRequiredService<IRigCatalog>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var configLogger = loggerFactory?.CreateLogger("HVO.SkyMonitor.CameraAdapters");
            var rigSpec = adapterOptions.ResolveRig(rigCatalog, configLogger);
            var observatoryClock = sp.GetRequiredService<IObservatoryClock>();

            if (CameraAdapterTypes.IsMockColor(adapterOptions.Adapter))
            {
                return new MockColorCameraAdapter(
                    sp.GetRequiredService<IOptionsMonitor<ObservatoryLocationOptions>>(),
                    sp.GetRequiredService<IOptionsMonitor<StarCatalogOptions>>(),
                    sp.GetRequiredService<IOptionsMonitor<CardinalDirectionsOptions>>(),
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    rigSpec,
                    observatoryClock,
                    loggerFactory,
                    sp.GetService<ILogger<MockColorCameraAdapter>>(),
                    noiseProfile: null,
                    preprocessingOrchestrator: sp.GetService<IFramePreprocessingOrchestrator>());
            }

            if (CameraAdapterTypes.IsMock(adapterOptions.Adapter))
            {
                return new MockCameraAdapter(
                    sp.GetRequiredService<IOptionsMonitor<ObservatoryLocationOptions>>(),
                    sp.GetRequiredService<IOptionsMonitor<StarCatalogOptions>>(),
                    sp.GetRequiredService<IOptionsMonitor<CardinalDirectionsOptions>>(),
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    rigSpec,
                    observatoryClock,
                    sp.GetService<ILogger<MockCameraAdapter>>(),
                    preprocessingOrchestrator: sp.GetService<IFramePreprocessingOrchestrator>());
            }

            if (CameraAdapterTypes.IsZwo(adapterOptions.Adapter))
            {
                return new ZwoCameraAdapter(
                    rigSpec,
                    observatoryClock,
                    sp.GetRequiredService<IOptionsMonitor<ObservatoryLocationOptions>>(),
                    sp.GetRequiredService<IOptionsMonitor<CardinalDirectionsOptions>>(),
                    loggerFactory,
                    sp.GetService<ILogger<ZwoCameraAdapter>>(),
                    sp.GetService<IFramePreprocessingOrchestrator>());
            }

            throw new InvalidOperationException($"Unsupported camera adapter type '{adapterOptions.Adapter}'.");
        });
    }

    private static void ConfigureLogging(ILoggingBuilder logging)
    {
        logging.AddConsole();
        logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Warning);

        logging.Services.AddSingleton<ILoggerProvider>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var configuredDirectory = configuration.GetValue<string>("SkyMonitor:Logging:Directory")
                ?? configuration.GetValue<string>("SkyMonitor__Logging__Directory")
                ?? "/var/hvo/logs";

            var directory = ResolveLoggingDirectory(configuredDirectory);

            var fileName = configuration.GetValue<string>("SkyMonitor:Logging:FileName")
                ?? configuration.GetValue<string>("SkyMonitor__Logging__FileName")
                ?? "skymonitor.log";

            var maxFileSizeMb = configuration.GetValue("SkyMonitor:Logging:MaxFileSizeMB", 10);
            var maxRetainedFiles = configuration.GetValue("SkyMonitor:Logging:MaxRetainedFiles", 5);

            var maxFileSizeBytes = Math.Max(1, maxFileSizeMb) * 1024L * 1024L;
            var retainedFiles = Math.Max(1, maxRetainedFiles);

            return new RollingFileLoggerProvider(directory, fileName, maxFileSizeBytes, retainedFiles, LogLevel.Information);
        });
    }

    private static string ResolveLoggingDirectory(string configuredPath)
    {
        if (TryEnsureDirectory(configuredPath, out var resolvedPath))
        {
            return resolvedPath;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var fallbackRoot = !string.IsNullOrWhiteSpace(localAppData)
            ? Path.Combine(localAppData, "HVO", "SkyMonitor", "logs")
            : Path.Combine(AppContext.BaseDirectory, "logs");

        if (TryEnsureDirectory(fallbackRoot, out resolvedPath))
        {
            Console.WriteLine($"[SkyMonitor] Falling back to writable logging directory '{resolvedPath}' because '{configuredPath}' is not accessible.");
            return resolvedPath;
        }

        Console.WriteLine($"[SkyMonitor] Unable to create logging directory '{configuredPath}' or fallback '{fallbackRoot}'. Defaulting to current directory.");
        return Directory.GetCurrentDirectory();
    }

    private static bool TryEnsureDirectory(string path, out string resolvedPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                resolvedPath = string.Empty;
                return false;
            }

            Directory.CreateDirectory(path);
            resolvedPath = path;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            resolvedPath = string.Empty;
            return false;
        }
    }

    private static void Configure(WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapOpenApi();

        var enableHttpsRedirect = app.Configuration.GetValue<bool?>("EnableHttpsRedirect") ?? false;

        if (app.Environment.IsDevelopment())
        {
            app.MapScalarApiReference();
            app.UseDeveloperExceptionPage();
        }
        else if (enableHttpsRedirect)
        {
            app.UseHsts();
        }

        if (enableHttpsRedirect)
        {
            app.UseHttpsRedirection();
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapControllers();
        app.MapPrometheusScrapingEndpoint("/metrics/prometheus");

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var clock = context.RequestServices.GetService<IObservatoryClock>();
                var payload = new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.Select(entry => new
                    {
                        name = entry.Key,
                        status = entry.Value.Status.ToString(),
                        description = entry.Value.Description,
                        duration = entry.Value.Duration,
                        tags = entry.Value.Tags
                    }),
                    timestamp = clock?.LocalNow ?? DateTimeOffset.Now
                };

                await context.Response.WriteAsJsonAsync(payload);
            }
        });

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("readiness")
        });
    }
}
