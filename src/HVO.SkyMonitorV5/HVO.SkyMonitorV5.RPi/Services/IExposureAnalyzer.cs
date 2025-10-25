#nullable enable
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Services;

/// <summary>
/// Performs lightweight analysis on captured frames to inform subsequent exposure decisions.
/// </summary>
public interface IExposureAnalyzer
{
    /// <summary>
    /// Analyses the captured frame and returns metrics plus any suggested exposure adjustments.
    /// </summary>
    ExposureAnalysisResult Analyze(CapturedImage capturedFrame, CameraConfiguration configuration);
}
