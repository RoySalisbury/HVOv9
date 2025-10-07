using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Data;

public interface IStarRepository
{
    Task<Result<IReadOnlyList<Star>>> GetVisibleStarsAsync(
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

    Task<Result<IReadOnlyList<Star>>> GetConstellationStarsAsync(string constellation3, double magnitudeLimit = 6.0);

    Task<Result<IReadOnlyList<HygStar>>> SearchByNameAsync(string query, int limit = 20);

    Task<Result<IReadOnlyList<Star>>> GetRaWindowAsync(double raStartHours, double raEndHours, double magnitudeLimit = 6.0);

    Task<Result<IReadOnlyList<Star>>> GetBrightestEverHighAsync(
        double latitudeDeg,
        double minMaxAltitudeDeg = 10.0,
        int topN = 200,
        double magnitudeLimit = 6.5);

    Task<Result<IReadOnlyDictionary<string, IReadOnlyList<ConstellationStar>>>> GetConstellationsAsync();

    Task<Result<IReadOnlyList<VisibleConstellation>>> GetVisibleByConstellationAsync(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        double magnitudeLimit = 6.5,
        double minMaxAltitudeDeg = 10.0,
        int screenWidth = 1,
        int screenHeight = 1);
}