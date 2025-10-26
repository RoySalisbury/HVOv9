using System;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Ensures frame export configuration includes archive payload scope when the image history archive is enabled.
/// </summary>
internal sealed class ImageHistoryFrameExportOptionsConfigurator : IPostConfigureOptions<FrameExportOptions>
{
    private readonly IOptions<ImageHistoryOptions> _imageHistoryOptions;

    public ImageHistoryFrameExportOptionsConfigurator(IOptions<ImageHistoryOptions> imageHistoryOptions)
    {
        _imageHistoryOptions = imageHistoryOptions ?? throw new ArgumentNullException(nameof(imageHistoryOptions));
    }

    public void PostConfigure(string? name, FrameExportOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var imageHistory = _imageHistoryOptions.Value ?? new ImageHistoryOptions();
        if (!imageHistory.EnableArchive)
        {
            return;
        }

        options.Processed ??= new FrameExportStageOptions();
        options.Processed.PayloadScope |= FrameExportPayloadScope.ArchiveOnly;
    }
}
