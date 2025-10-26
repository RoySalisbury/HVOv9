using System;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;

namespace HVO.SkyMonitorV5.RPi.Services;

/// <summary>
/// Specifies the context for encoding processed frames.
/// </summary>
public enum ProcessedFrameEncodingContext
{
    /// <summary>For UI display and API responses - should use JPG/PNG formats.</summary>
    UserInterface,
    /// <summary>For export/archive purposes - can use FITS when configured.</summary>
    Export
}

/// <summary>
/// Encodes processed frames into delivery-ready payloads using the configured export settings.
/// </summary>
public interface IProcessedFrameEncoder
{
    /// <summary>
    /// Encodes the specified <paramref name="frame"/> into the configured delivery format.
    /// </summary>
    /// <param name="frame">The processed frame to encode.</param>
    /// <param name="context">The encoding context (UI vs Export) to determine format behavior.</param>
    /// <param name="customEncoding">Optional custom encoding settings to override frame defaults.</param>
    /// <returns>The encoded payload, including content type metadata.</returns>
    ProcessedFrameDelivery Encode(
        ProcessedFrame frame,
        ProcessedFrameEncodingContext context = ProcessedFrameEncodingContext.UserInterface,
        ImageEncodingSettings? customEncoding = null);
}

/// <summary>
/// Represents an encoded processed frame payload.
/// </summary>
/// <param name="Payload">Encoded image bytes.</param>
/// <param name="ContentType">Content type to advertise for the payload.</param>
/// <param name="FileExtension">Suggested file extension (without dot) for persisted payloads.</param>
public readonly record struct ProcessedFrameDelivery(
    ReadOnlyMemory<byte> Payload,
    string ContentType,
    string? FileExtension);