using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Response model for sequence JSON data
/// </summary>
public record SequenceJsonResponse
{
    [JsonPropertyName("Response")]
    public List<SequenceItem>? Response { get; init; }

    [JsonPropertyName("Error")]
    public string? Error { get; init; }

    [JsonPropertyName("StatusCode")]
    public int StatusCode { get; init; }

    [JsonPropertyName("Success")]
    public bool Success { get; init; }

    [JsonPropertyName("Type")]
    public string? Type { get; init; }
}

/// <summary>
/// Represents a sequence item that can be either an instruction or container
/// </summary>
public record SequenceItem
{
    [JsonPropertyName("Conditions")]
    public List<object>? Conditions { get; init; }

    [JsonPropertyName("Items")]
    public List<object>? Items { get; init; }

    [JsonPropertyName("Triggers")]
    public List<object>? Triggers { get; init; }

    [JsonPropertyName("Status")]
    public string? Status { get; init; }

    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("GlobalTriggers")]
    public List<object>? GlobalTriggers { get; init; }
}
