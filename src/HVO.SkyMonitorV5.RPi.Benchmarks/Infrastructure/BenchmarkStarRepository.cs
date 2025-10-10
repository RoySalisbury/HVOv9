using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Data;

namespace HVO.SkyMonitorV5.RPi.Benchmarks.Infrastructure;

internal sealed class BenchmarkStarRepository : IStarRepository
{
    private static readonly Result<IReadOnlyList<Star>> EmptyStarList = Result<IReadOnlyList<Star>>.Success(Array.Empty<Star>());
    private static readonly Result<IReadOnlyDictionary<string, IReadOnlyList<Star>>> EmptyConstellations = Result<IReadOnlyDictionary<string, IReadOnlyList<Star>>>.Success(new Dictionary<string, IReadOnlyList<Star>>());
    private static readonly Result<IReadOnlyList<HygStar>> EmptyHygList = Result<IReadOnlyList<HygStar>>.Success(Array.Empty<HygStar>());
    private static readonly Result<IReadOnlyList<VisibleConstellation>> EmptyVisibleConstellations = Result<IReadOnlyList<VisibleConstellation>>.Success(Array.Empty<VisibleConstellation>());

    public Task<Result<IReadOnlyList<Star>>> GetVisibleStarsAsync(double latitudeDeg, double longitudeDeg, DateTime utc, double magnitudeLimit = 6.5, double minMaxAltitudeDeg = 10, int topN = 300, bool stratified = false, int raBins = 24, int decBands = 8, int screenWidth = 1, int screenHeight = 1, StarFieldEngine? engine = null)
        => Task.FromResult(EmptyStarList);

    public Task<Result<IReadOnlyList<Star>>> GetConstellationStarsAsync(string constellation3, double magnitudeLimit = 6)
        => Task.FromResult(EmptyStarList);

    public Task<Result<IReadOnlyList<HygStar>>> SearchByNameAsync(string query, int limit = 20)
        => Task.FromResult(EmptyHygList);

    public Task<Result<IReadOnlyList<Star>>> GetRaWindowAsync(double raStartHours, double raEndHours, double magnitudeLimit = 6)
        => Task.FromResult(EmptyStarList);

    public Task<Result<IReadOnlyList<Star>>> GetBrightestEverHighAsync(double latitudeDeg, double minMaxAltitudeDeg = 10, int topN = 200, double magnitudeLimit = 6.5)
        => Task.FromResult(EmptyStarList);

    public Task<Result<IReadOnlyDictionary<string, IReadOnlyList<Star>>>> GetConstellationsAsync()
        => Task.FromResult(EmptyConstellations);

    public Task<Result<IReadOnlyList<VisibleConstellation>>> GetVisibleByConstellationAsync(double latitudeDeg, double longitudeDeg, DateTime utc, double magnitudeLimit = 6.5, double minMaxAltitudeDeg = 10, int screenWidth = 1, int screenHeight = 1, StarFieldEngine? engine = null)
        => Task.FromResult(EmptyVisibleConstellations);
}
