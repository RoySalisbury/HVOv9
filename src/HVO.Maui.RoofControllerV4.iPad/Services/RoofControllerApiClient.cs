using System.Net.Http.Json;
using System.Text.Json;
using HVO.Maui.RoofControllerV4.iPad.Configuration;
using HVO.WebSite.RoofControllerV4.Models;
using HVO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Maui.RoofControllerV4.iPad.Services;

/// <summary>
/// Typed HTTP client for interacting with the Roof Controller Web API.
/// </summary>
public sealed class RoofControllerApiClient : IRoofControllerApiClient
{
    private readonly HttpClient _httpClient;
    private readonly RoofControllerApiOptions _options;
    private readonly ILogger<RoofControllerApiClient> _logger;

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RoofControllerApiClient(HttpClient httpClient, IOptions<RoofControllerApiOptions> options, ILogger<RoofControllerApiClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        EnsureBaseAddress();
    }

    public Task<Result<RoofStatusResponse>> GetStatusAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<RoofStatusResponse>(HttpMethod.Get, "RoofControl/Status", cancellationToken: cancellationToken);

    public Task<Result<RoofStatusResponse>> OpenAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<RoofStatusResponse>(HttpMethod.Get, "RoofControl/Open", cancellationToken: cancellationToken);

    public Task<Result<RoofStatusResponse>> CloseAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<RoofStatusResponse>(HttpMethod.Get, "RoofControl/Close", cancellationToken: cancellationToken);

    public Task<Result<RoofStatusResponse>> StopAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<RoofStatusResponse>(HttpMethod.Get, "RoofControl/Stop", cancellationToken: cancellationToken);

    public Task<Result<bool>> ClearFaultAsync(int? pulseMs = null, CancellationToken cancellationToken = default)
    {
        var query = pulseMs.HasValue ? $"ClearFault?pulseMs={pulseMs.Value}" : "ClearFault";
        return SendRequestAsync<bool>(HttpMethod.Post, $"RoofControl/{query}", cancellationToken: cancellationToken);
    }

    private async Task<Result<T>> SendRequestAsync<T>(HttpMethod method, string relativeUrl, HttpContent? content = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUrl);

        try
        {
            using var request = new HttpRequestMessage(method, relativeUrl)
            {
                Content = content
            };

            _logger.LogDebug("Sending {Method} request to {Endpoint}", method, relativeUrl);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await TryReadProblemDetailsAsync(response, cancellationToken).ConfigureAwait(false);
                var exception = new HttpRequestException($"API request to '{relativeUrl}' failed with {(int)response.StatusCode} {response.ReasonPhrase}: {errorText}");
                _logger.LogError(exception, "Roof controller API call failed");
                return Result<T>.Failure(exception);
            }

            if (response.Content.Headers.ContentLength == 0)
            {
                if (typeof(T) == typeof(bool))
                {
                    return Result<T>.Success((T)(object)true);
                }

                var noContent = new InvalidOperationException($"API response from '{relativeUrl}' was empty.");
                _logger.LogError(noContent, "Unexpected empty response.");
                return Result<T>.Failure(noContent);
            }

            var result = await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                var nullContent = new InvalidOperationException($"API response from '{relativeUrl}' could not be deserialized to {typeof(T).Name}.");
                _logger.LogError(nullContent, "Failed to deserialize API response");
                return Result<T>.Failure(nullContent);
            }

            return Result<T>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error calling Roof Controller API {Method} {Endpoint}", method, relativeUrl);
            return Result<T>.Failure(ex);
        }
    }

    private async Task<string> TryReadProblemDetailsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
            {
                return "<no error body>";
            }

            try
            {
                var problem = JsonSerializer.Deserialize<ProblemDetailsPayload>(content, JsonSerializerOptions);
                if (problem is not null)
                {
                    return problem.Detail ?? problem.Title ?? content;
                }
            }
            catch (JsonException)
            {
                // ignored, we fall back to raw content
            }

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read problem details from API response");
            return "<unable to read error body>";
        }
    }

    private void EnsureBaseAddress()
    {
        if (_httpClient.BaseAddress is not null)
        {
            return;
        }

        var baseUri = _options.GetBaseUri();
        if (!baseUri.AbsoluteUri.EndsWith('/'))
        {
            baseUri = new Uri(baseUri.AbsoluteUri + "/", UriKind.Absolute);
        }

        _httpClient.BaseAddress = baseUri;
    }

    private sealed record ProblemDetailsPayload(string? Title, string? Detail);
}
