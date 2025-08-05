using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Camera capture response - handles both simple string responses and complex object responses
/// Based on NINA API specification which can return either string status or capture data
/// </summary>
public class CaptureResponseWrapper
{
    /// <summary>
    /// The raw response from NINA API (can be string or CaptureResponse object)
    /// </summary>
    public object? Response { get; set; }
    
    /// <summary>
    /// True if the response is a simple string status message
    /// </summary>
    public bool IsStringResponse => Response is string;
    
    /// <summary>
    /// True if the response is a complex capture result object
    /// </summary>
    public bool IsCaptureResponse => Response is CaptureResponse;
    
    /// <summary>
    /// Get the string response (when IsStringResponse is true)
    /// Examples: "Capture already in progress", "Capture started"
    /// </summary>
    public string? StringResponse => Response as string;
    
    /// <summary>
    /// Get the capture response object (when IsCaptureResponse is true)
    /// </summary>
    public CaptureResponse? CaptureResponseData => Response as CaptureResponse;
}

/// <summary>
/// Camera capture response - for complex responses with image data and platesolve results
/// Based on NINA API specification for waitForResult=true responses
/// </summary>
public record CaptureResponse
{
    [JsonPropertyName("Image")]
    public string? Image { get; init; }

    [JsonPropertyName("PlateSolveResult")]
    public PlatesolveResult? PlateSolveResult { get; init; }
}