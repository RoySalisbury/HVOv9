using System;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Models.ImageHistory;

namespace HVO.SkyMonitorV5.RPi.Services;

public interface IImageHistoryService
{
    Task<Result<ImageHistoryThumbnailPage>> GetThumbnailsAsync(ImageHistoryThumbnailsRequest request, CancellationToken cancellationToken);

    Task<Result<ImageHistoryFrameDetailResult>> GetFrameAsync(Guid frameId, CancellationToken cancellationToken);

    Task<Result<ImageHistoryStatsResponse>> GetStatsAsync(ImageHistoryStatsRequest request, CancellationToken cancellationToken);
}
