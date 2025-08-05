namespace HVO.NinaClient;

/// <summary>
/// Configuration options for NINA API client
/// </summary>
public class NinaApiClientOptions
{
    /// <summary>
    /// Base URL for NINA API (e.g., "http://localhost:1888")
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:1888";

    /// <summary>
    /// API key for authentication (if required)
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300; // Increased to 5 minutes for long astronomy operations

    /// <summary>
    /// Maximum number of retry attempts for failed requests
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts in milliseconds
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;
}
