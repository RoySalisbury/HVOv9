using System;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Services;

/// <summary>
/// Encodes processed frames into delivery-ready payloads using the configured export settings.
/// </summary>
public interface IProcessedFrameEncoder
{
    /// <summary>
    /// Encodes the specified <paramref name="frame"/> into the configured delivery format.
    /// </summary>
    /// <param name="frame">The processed frame to encode.</param>
    /// <returns>The encoded payload, including content type metadata.</returns>
    ProcessedFrameDelivery Encode(ProcessedFrame frame);
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
