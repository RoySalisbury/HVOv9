using System;
using HVO.SkyMonitorV5.RPi.Exports;

namespace HVO.SkyMonitorV5.RPi.ImageHistory;

/// <summary>
/// Represents a processed frame that should be recorded in the image history archive.
/// </summary>
public sealed record ImageFrameArchiveIngestionRequest(
    Guid FrameId,
    FrameExportMetadata Metadata,
    byte[] Payload,
    string ContentType,
    string FileExtension,
    FrameExportPayloadRole PayloadRole);
