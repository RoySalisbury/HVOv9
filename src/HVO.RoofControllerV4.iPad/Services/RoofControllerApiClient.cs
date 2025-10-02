using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HVO.RoofControllerV4.iPad.Configuration;
using HVO.RoofControllerV4.Common.Models;
using HVO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.RoofControllerV4.iPad.Services;

/// <summary>
/// Typed HTTP client for interacting with the Roof Controller Web API.
/// </summary>
public sealed class RoofControllerApiClient : IRoofControllerApiClient
{
    private readonly HttpClient _httpClient;
    private readonly RoofControllerApiOptions _options;
    private readonly ILogger<RoofControllerApiClient> _logger;

    private static readonly JsonSerializerOptions JsonSerializerOptions = CreateSerializerOptions();

    public RoofControllerApiClient(HttpClient httpClient, IOptions<RoofControllerApiOptions> options, ILogger<RoofControllerApiClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        EnsureBaseAddress();
    }

    public Task<Result<RoofStatusResponse>> GetStatusAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<RoofStatusResponse>(HttpMethod.Get, "roofcontrol/status", cancellationToken: cancellationToken);

    public Task<Result<RoofStatusResponse>> OpenAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<RoofStatusResponse>(HttpMethod.Get, "roofcontrol/open", cancellationToken: cancellationToken);

    public Task<Result<RoofStatusResponse>> CloseAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<RoofStatusResponse>(HttpMethod.Get, "roofcontrol/close", cancellationToken: cancellationToken);

    public Task<Result<RoofStatusResponse>> StopAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<RoofStatusResponse>(HttpMethod.Get, "roofcontrol/stop", cancellationToken: cancellationToken);

    public Task<Result<bool>> ClearFaultAsync(int? pulseMs = null, CancellationToken cancellationToken = default)
    {
        var query = pulseMs.HasValue ? $"clearfault?pulseMs={pulseMs.Value}" : "clearfault";
        return SendRequestAsync<bool>(HttpMethod.Post, $"roofcontrol/{query}", cancellationToken: cancellationToken);
    }

    public Task<Result<RoofConfigurationResponse>> GetConfigurationAsync(CancellationToken cancellationToken = default)
        => SendRequestAsync<RoofConfigurationResponse>(HttpMethod.Get, "roofcontrol/configuration", cancellationToken: cancellationToken);

    public Task<Result<RoofConfigurationResponse>> UpdateConfigurationAsync(RoofConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var content = JsonContent.Create(request, options: JsonSerializerOptions);
        return SendRequestAsync<RoofConfigurationResponse>(HttpMethod.Post, "roofcontrol/configuration", content, cancellationToken);
    }

    private async Task<Result<T>> SendRequestAsync<T>(HttpMethod method, string relativeUrl, HttpContent? content = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUrl);

        var attempt = 0;
        var maxAttempts = Math.Max(1, _options.RequestRetryCount);

        while (true)
        {
            attempt++;

            try
            {
                using var request = new HttpRequestMessage(method, relativeUrl)
                {
                    Content = content
                };

                _logger.LogDebug("Sending {Method} request to {Endpoint} (attempt {Attempt}/{MaxAttempts})", method, relativeUrl, attempt, maxAttempts);

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
                if (attempt >= maxAttempts || cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Unhandled error calling Roof Controller API {Method} {Endpoint} after {Attempts} attempts", method, relativeUrl, attempt);
                    return Result<T>.Failure(ex);
                }

                var delay = TimeSpan.FromMilliseconds(Math.Min(500 * attempt, 2_000));
                _logger.LogWarning(ex, "Error calling Roof Controller API {Method} {Endpoint} on attempt {Attempt}. Retrying in {Delay}ms", method, relativeUrl, attempt, delay.TotalMilliseconds);

                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return Result<T>.Failure(ex);
                }
            }
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

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));

        return options;
    }
}
