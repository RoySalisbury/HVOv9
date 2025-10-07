using System.Globalization;
using System.IO;
using System.Reflection;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Cameras.MockCamera;

/// <summary>
/// Legacy embedded star catalog implementation has been removed in favor of the database-backed IStarRepository.
/// This stub remains to prevent accidental reintroduction of the static catalog.
/// </summary>
internal static class StarCatalog
{
}
