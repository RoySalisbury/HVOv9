using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HVO.Core.Results;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Data;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Pipeline.Filters;

[TestClass]
public sealed class CelestialAnnotationsFilterTests
{
    [TestMethod]
    public async Task ApplyAsync_FilterFrame_RendersConfiguredStarAnnotation()
    {
        var latitude = 35.1987;
        var longitude = -114.0539;
        var timestamp = DateTimeOffset.Parse("2025-03-02T06:45:00Z", CultureInfo.InvariantCulture);
        var lstHours = StarFieldEngine.LocalSiderealTime(timestamp.UtcDateTime, longitude);

        var annotatedStar = new Star(
            RightAscensionHours: lstHours,
            DeclinationDegrees: latitude - 5.0,
            Magnitude: 2.2,
            Color: SKColors.LightGoldenrodYellow,
            CommonName: "Test Star");

        var services = new ServiceCollection();
        services.AddSingleton<IConstellationCatalog>(new TestConstellationCatalog(annotatedStar));
        services.AddSingleton<IStarRepository>(new NoOpStarRepository());
        services.AddSingleton<IPlanetRepository>(new NoOpPlanetRepository());

        using var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var annotationOptions = new CelestialAnnotationsOptions
        {
            UseAutomaticStarSelection = false,
            AnnotatePlanets = false,
            LabelFontSize = 12f,
            StarRingRadius = 6f
        };
        annotationOptions.StarNames.Add("Test Star");

        using var annotationMonitor = new TestOptionsMonitor<CelestialAnnotationsOptions>(annotationOptions);

        var catalogOptions = new StarCatalogOptions
        {
            IncludePlanets = false,
            IncludeMoon = false,
            IncludeOuterPlanets = false,
            IncludeSun = false
        };
        using var catalogMonitor = new TestOptionsMonitor<StarCatalogOptions>(catalogOptions);

        var filter = new CelestialAnnotationsFilter(catalogMonitor, annotationMonitor, scopeFactory, NullLogger<CelestialAnnotationsFilter>.Instance);

        var (frameContext, renderContext) = CreateRenderContext(latitude, longitude, timestamp);
        var engine = renderContext.Engine;
        var width = engine.Width;
        var height = engine.Height;

        Assert.IsTrue(engine.ProjectStar(annotatedStar, out var starX, out var starY), "Annotated star should project within frame bounds.");

        using var colorSpace = SKColorSpace.CreateSrgbLinear();
        using var stackedBitmap = SkiaTestImageFactory.CreateLinearGradientBitmap(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            colorSpace,
            redScale: 0.33f,
            greenScale: 0.52f,
            blueValue: 0.44f);

        using var gradientImage = SkiaTestImageFactory.CreateLinearGradientImage(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            colorSpace,
            redScale: 0.33f,
            greenScale: 0.52f,
            blueValue: 0.44f);

        using var surfacePool = new SkiaSurfacePool();
        var surfaceLease = surfacePool.RentLinearSurface(width, height);
        var surface = surfaceLease.Surface;
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(gradientImage, 0, 0);
        surface.Canvas.Flush();

        using var filterFrame = new FilterFrame(surfaceLease);

        var stackResult = CreateStackResult(stackedBitmap, frameContext);
        var configuration = CreateConfiguration();

        Assert.IsTrue(filter.ShouldApply(configuration), "Filter should apply when star annotations are configured.");

        using var baselineImage = filterFrame.SnapshotImage();
        var baseline = SkiaTestImageFactory.GetNormalizedFloatPixelBuffer(baselineImage, SKColorType.Bgra8888);

        await filter.ApplyAsync(filterFrame, stackResult, configuration, renderContext, CancellationToken.None);

        using var processedImage = filterFrame.SnapshotImage();
        var processed = SkiaTestImageFactory.GetNormalizedFloatPixelBuffer(processedImage, SKColorType.Bgra8888);

        var channels = 4;
        var rowStride = width * channels;
        var tolerance = 2f / 255f;

        var changeDetected = false;
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        var maxDeltaObserved = 0f;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowStride;
            for (var x = 0; x < width; x++)
            {
                var pixelOffset = rowOffset + x * channels;
                for (var channel = 0; channel < 3; channel++)
                {
                    var delta = Math.Abs(baseline[pixelOffset + channel] - processed[pixelOffset + channel]);
                    if (delta > maxDeltaObserved)
                    {
                        maxDeltaObserved = delta;
                    }

                    if (delta > tolerance)
                    {
                        changeDetected = true;
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                        break;
                    }
                }
            }
        }

        Assert.IsTrue(changeDetected, $"Celestial annotation overlay should modify the frame (max delta observed {maxDeltaObserved:F6}).");
        Assert.IsTrue(minX <= starX && starX <= maxX, "Annotated region should encompass the projected star along the X axis.");
        Assert.IsTrue(minY <= starY && starY <= maxY, "Annotated region should encompass the projected star along the Y axis.");

        frameContext.Dispose();
    }

    private static (FrameContext Context, FrameRenderContext RenderContext) CreateRenderContext(double latitude, double longitude, DateTimeOffset timestamp)
    {
        var rig = RigPresets.MockAsi174_Fujinon;
        var engine = new StarFieldEngine(rig, latitude, longitude, timestamp.UtcDateTime, flipHorizontal: false, applyRefraction: true, horizonPadding: 0.95);
        var context = new FrameContext(
            Guid.NewGuid(),
            rig,
            engine,
            timestamp,
            latitude,
            longitude,
            FlipHorizontal: false,
            HorizonPadding: 0.95,
            ApplyRefraction: true,
            DisposeAction: _ => { });

        return (context, new FrameRenderContext(context));
    }

    private static FrameStackResult CreateStackResult(SKBitmap stackedBitmap, FrameContext frameContext)
    {
        var frameId = Guid.NewGuid();
        var exposure = new ExposureSettings(600, 180, false, false);

        return new FrameStackResult(
            frameId,
            stackedBitmap,
            stackedBitmap,
            DateTimeOffset.Parse("2025-03-02T06:45:00Z", CultureInfo.InvariantCulture),
            exposure,
            frameContext,
            FramesStacked: 4,
            IntegrationMilliseconds: 2400);
    }

    private static CameraConfiguration CreateConfiguration()
        => new(
            EnableStacking: true,
            StackingFrameCount: 3,
            EnableImageOverlays: true,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 3,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());

    private sealed class TestConstellationCatalog : IConstellationCatalog
    {
        private readonly IReadOnlyList<ConstellationFigure> _figures;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<Star>> _starLookup;

        public TestConstellationCatalog(Star star)
        {
            _figures = Array.Empty<ConstellationFigure>();
            _starLookup = new Dictionary<string, IReadOnlyList<Star>>(StringComparer.OrdinalIgnoreCase)
            {
                ["TES"] = new[] { star }
            };
        }

        public IReadOnlyList<ConstellationFigure> GetFigures() => _figures;

        public IReadOnlyDictionary<string, IReadOnlyList<Star>> GetStarLookup() => _starLookup;
    }

    private sealed class NoOpStarRepository : IStarRepository
    {
        public Task<Result<IReadOnlyDictionary<string, IReadOnlyList<Star>>>> GetConstellationsAsync()
            => Task.FromResult(Result<IReadOnlyDictionary<string, IReadOnlyList<Star>>>.Failure(new NotImplementedException()));

        public Task<Result<IReadOnlyList<Star>>> GetBrightestEverHighAsync(double latitudeDeg, double minMaxAltitudeDeg = 10.0, int topN = 200, double magnitudeLimit = 6.5)
            => Task.FromResult(Result<IReadOnlyList<Star>>.Failure(new NotImplementedException()));

        public Task<Result<IReadOnlyList<Star>>> GetConstellationStarsAsync(string constellation3, double magnitudeLimit = 6.0)
            => Task.FromResult(Result<IReadOnlyList<Star>>.Failure(new NotImplementedException()));

        public Task<Result<IReadOnlyList<VisibleConstellation>>> GetVisibleByConstellationAsync(double latitudeDeg, double longitudeDeg, DateTime utc, double magnitudeLimit = 6.5, double minMaxAltitudeDeg = 10.0, int screenWidth = 1, int screenHeight = 1, StarFieldEngine? engine = null)
            => Task.FromResult(Result<IReadOnlyList<VisibleConstellation>>.Failure(new NotImplementedException()));

        public Task<Result<IReadOnlyList<Star>>> GetVisibleStarsAsync(double latitudeDeg, double longitudeDeg, DateTime utc, double magnitudeLimit = 6.5, double minMaxAltitudeDeg = 10.0, int topN = 300, bool stratified = false, int raBins = 24, int decBands = 8, int screenWidth = 1, int screenHeight = 1, StarFieldEngine? engine = null)
            => Task.FromResult(Result<IReadOnlyList<Star>>.Success(Array.Empty<Star>()));

        public Task<Result<IReadOnlyList<Star>>> GetRaWindowAsync(double raStartHours, double raEndHours, double magnitudeLimit = 6.0)
            => Task.FromResult(Result<IReadOnlyList<Star>>.Failure(new NotImplementedException()));

        public Task<Result<IReadOnlyList<HVO.SkyMonitorV5.Data.Catalogs.Hyg.HygStar>>> SearchByNameAsync(string query, int limit = 20)
            => Task.FromResult(Result<IReadOnlyList<HVO.SkyMonitorV5.Data.Catalogs.Hyg.HygStar>>.Failure(new NotImplementedException()));
    }

    private sealed class NoOpPlanetRepository : IPlanetRepository
    {
        public Task<Result<IReadOnlyList<PlanetMark>>> GetVisiblePlanetsAsync(double latitudeDeg, double longitudeDeg, DateTime utc, PlanetVisibilityCriteria criteria, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<PlanetMark>>.Success(Array.Empty<PlanetMark>()));
    }
}
