using System;
using System.Threading.Channels;

namespace HVO.SkyMonitorV5.RPi.Exports;

/// <summary>
/// Configures the frame export dispatcher channel behavior.
/// </summary>
public sealed class FrameExportDispatcherOptions
{
    public const string SectionName = "FrameExportDispatcher";

    /// <summary>
    /// Gets or sets the channel capacity. Defaults to 8.
    /// </summary>
    public int ChannelCapacity { get; set; } = 8;

    /// <summary>
    /// Gets or sets the bounded channel full mode. Defaults to <see cref="FrameExportChannelFullMode.DropNewest"/>.
    /// </summary>
    public FrameExportChannelFullMode FullMode { get; set; } = FrameExportChannelFullMode.DropNewest;

    /// <summary>
    /// Gets or sets the maximum number of concurrent sink dispatch operations.
    /// </summary>
    public int MaxConcurrency { get; set; } = 2;

    /// <summary>
    /// Gets or sets the time allowed for the dispatcher to complete outstanding work during shutdown.
    /// </summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal BoundedChannelFullMode ToBoundedMode() => FullMode switch
    {
        FrameExportChannelFullMode.DropOldest => BoundedChannelFullMode.DropOldest,
        FrameExportChannelFullMode.DropNewest => BoundedChannelFullMode.DropNewest,
        _ => BoundedChannelFullMode.Wait
    };
}

/// <summary>
/// Defines channel overflow behavior for the export dispatcher.
/// </summary>
public enum FrameExportChannelFullMode
{
    Wait = 0,
    DropNewest = 1,
    DropOldest = 2
}
