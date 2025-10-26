#nullable enable
using Minio;

namespace HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;

/// <summary>
/// Provides configured MinIO clients for remote dispatch operations.
/// </summary>
public interface IMinioClientProvider
{
    /// <summary>
    /// Gets or creates an <see cref="IMinioClient"/> using the specified connection settings.
    /// </summary>
    IMinioClient GetClient(string endpoint, string accessKey, string secretKey, bool useSsl);
}
