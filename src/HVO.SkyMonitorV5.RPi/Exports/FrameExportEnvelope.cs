using System;

namespace HVO.SkyMonitorV5.RPi.Exports;

/// <summary>
/// Represents a frame payload ready to be delivered to export sinks.
/// </summary>
public sealed record FrameExportEnvelope(
    Guid FrameId,
    FrameExportStage Stage,
    FrameExportMetadata Metadata,
    ReadOnlyMemory<byte> Payload,
    string ContentType,
    string? FileExtension = null);
