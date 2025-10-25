using System;

namespace HVO.SkyMonitorV5.RPi.Infrastructure;

public sealed class LocalApiClientOptions
{
    public const string SectionName = "LocalApi";

    /// <summary>
    /// Base address for the SkyMonitor local API. When empty the client attempts to use the current navigation base URI.
    /// </summary>
    public string? BaseAddress { get; set; }

    /// <summary>
    /// Optional API key supplied on each request to the local API.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Header name used when applying the API key. Defaults to <c>X-Api-Key</c>.
    /// </summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    /// Request timeout applied to the underlying HttpClient.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
