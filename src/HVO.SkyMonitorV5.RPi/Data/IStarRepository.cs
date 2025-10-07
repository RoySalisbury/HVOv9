using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras.MockCamera;

namespace HVO.SkyMonitorV5.RPi.Data;

public interface IStarRepository
{
    Task<List<Star>> GetVisibleStarsAsync(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        double magnitudeLimit = 6.5,
        double minMaxAltitudeDeg = 10.0,
        int topN = 300,
        bool stratified = false,
        int raBins = 24,
        int decBands = 8,
        int screenWidth = 1,
        int screenHeight = 1);

    Task<List<Star>> GetConstellationStarsAsync(string constellation3, double magnitudeLimit = 6.0);

    Task<List<HygStar>> SearchByNameAsync(string query, int limit = 20);

    Task<List<Star>> GetRaWindowAsync(double raStartHours, double raEndHours, double magnitudeLimit = 6.0);

    Task<List<Star>> GetBrightestEverHighAsync(
        double latitudeDeg,
        double minMaxAltitudeDeg = 10.0,
        int topN = 200,
        double magnitudeLimit = 6.5);

    Task<IReadOnlyDictionary<string, IReadOnlyList<ConstellationStar>>> GetConstellationsAsync();

    Task<List<VisibleConstellation>> GetVisibleByConstellationAsync(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        double magnitudeLimit = 6.5,
        double minMaxAltitudeDeg = 10.0,
        int screenWidth = 1,
        int screenHeight = 1);
}

public sealed class VisibleConstellation
{
    public string ConstellationCode { get; init; } = "";
    public List<Star> Stars { get; init; } = new();
}