using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Infrastructure;

using HVO.SkyMonitorV5.RPi.Models.Cameras;
using HVO.SkyMonitorV5.RPi.Models.Catalog;
using HVO.SkyMonitorV5.RPi.Models.Optics;
using HVO.SkyMonitorV5.RPi.Models.Rigs;
using HVO.SkyMonitorV5.RPi.Models.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Services;

public interface ILocalApiClient
{
    Task<LocalApiFrameResponse?> GetLatestProcessedFrameAsync(CancellationToken cancellationToken);

    Task<LocalApiFrameResponse?> GetLatestRawFrameAsync(string? format, CancellationToken cancellationToken);

    Task<SystemObservatoryConfigurationResponse?> GetSystemObservatoryAsync(CancellationToken cancellationToken);

    Task<SystemObservatoryConfigurationResponse?> UpdateSystemObservatoryAsync(UpdateSystemObservatoryRequest request, CancellationToken cancellationToken);

    Task<SystemLocalApiConfigurationResponse?> GetSystemLocalApiAsync(CancellationToken cancellationToken);

    Task<SystemLocalApiConfigurationResponse?> UpdateSystemLocalApiAsync(UpdateSystemLocalApiRequest request, CancellationToken cancellationToken);

    Task<SystemTelemetryRetentionConfigurationResponse?> GetTelemetryRetentionAsync(CancellationToken cancellationToken);

    Task<SystemTelemetryRetentionConfigurationResponse?> UpdateTelemetryRetentionAsync(UpdateSystemTelemetryRetentionRequest request, CancellationToken cancellationToken);

    Task<EquipmentCatalogResponse?> GetEquipmentCatalogAsync(CancellationToken cancellationToken);

    Task<CameraDriverCatalogResponse?> GetCameraDriverCatalogAsync(CancellationToken cancellationToken);

    Task<EquipmentCatalogResponse?> CreateRigAsync(CreateRigRequest request, CancellationToken cancellationToken);

    Task<EquipmentCatalogResponse?> UpdateRigAsync(int rigId, UpdateRigRequest request, CancellationToken cancellationToken);

    Task<EquipmentCatalogResponse?> DeleteRigAsync(int rigId, long? revision, CancellationToken cancellationToken);



    Task<EquipmentCatalogResponse?> CreateCameraAsync(CreateCameraRequest request, CancellationToken cancellationToken);

    Task<EquipmentCatalogResponse?> UpdateCameraAsync(int cameraId, UpdateCameraRequest request, CancellationToken cancellationToken);

    Task<EquipmentCatalogResponse?> CreateOpticsAsync(CreateOpticsRequest request, CancellationToken cancellationToken);

    Task<EquipmentCatalogResponse?> UpdateOpticsAsync(int opticsId, UpdateOpticsRequest request, CancellationToken cancellationToken);
}

public sealed record LocalApiFrameResponse(
    Guid? FrameId,
    DateTimeOffset? Timestamp,
    byte[] Payload,
    string? ContentType,
    string? FileExtension,
    FrameExportImageDescriptor? Descriptor);

internal sealed class LocalApiClient : ILocalApiClient, IDisposable
{
    private static readonly string[] EmptyValues = Array.Empty<string>();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly LocalApiClientOptions _options;
    private readonly ILogger<LocalApiClient> _logger;

    private bool _disposed;

    private const string ProcessedFrameIdHeader = "X-HVO-Processed-FrameId";
    private const string ProcessedTimestampHeader = "X-HVO-Processed-TimestampUtc";
    private const string RawFrameIdHeader = "X-HVO-Raw-FrameId";
    private const string RawTimestampHeader = "X-HVO-Raw-TimestampUtc";
    private const string RawPixelFormatHeader = "X-HVO-Raw-PixelFormat";

    public LocalApiClient(HttpClient httpClient, IOptions<LocalApiClientOptions> optionsAccessor, ILogger<LocalApiClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = optionsAccessor?.Value ?? throw new ArgumentNullException(nameof(optionsAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LocalApiFrameResponse?> GetLatestProcessedFrameAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1.0/all-sky/frame/latest");
        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API processed frame request failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (payload.Length == 0)
        {
            _logger.LogWarning("Local API processed frame request returned an empty payload.");
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.ToString();
        var frameId = ReadGuidHeader(response, ProcessedFrameIdHeader);
        var timestamp = ReadDateTimeOffsetHeader(response, ProcessedTimestampHeader);

        var fileExtension = TryResolveExtensionFromContentType(contentType);

        return new LocalApiFrameResponse(frameId, timestamp, payload, contentType, fileExtension, null);
    }

    public async Task<LocalApiFrameResponse?> GetLatestRawFrameAsync(string? format, CancellationToken cancellationToken)
    {
        var uri = string.IsNullOrWhiteSpace(format)
            ? "api/v1.0/all-sky/frame/latest?raw=true"
            : $"api/v1.0/all-sky/frame/latest?raw=true&rawFormat={Uri.EscapeDataString(format)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API raw frame request failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (payload.Length == 0)
        {
            _logger.LogWarning("Local API raw frame request returned an empty payload for format {Format}.", format);
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.ToString();
        var frameId = ReadGuidHeader(response, RawFrameIdHeader);
        var timestamp = ReadDateTimeOffsetHeader(response, RawTimestampHeader);

        var descriptor = FrameExportHeaderParser.TryCreateDescriptor(response.Headers);
        var fileExtension = TryResolveExtensionFromFormat(format, contentType);

        return new LocalApiFrameResponse(frameId, timestamp, payload, contentType, fileExtension, descriptor);
    }

    public async Task<SystemObservatoryConfigurationResponse?> GetSystemObservatoryAsync(CancellationToken cancellationToken)
    {
    using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1.0/configuration/system/observatory");
        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API observatory request failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

        return await ReadJsonAsync<SystemObservatoryConfigurationResponse>(response, "observatory", cancellationToken).ConfigureAwait(false);
    }

    public async Task<SystemObservatoryConfigurationResponse?> UpdateSystemObservatoryAsync(UpdateSystemObservatoryRequest requestModel, CancellationToken cancellationToken)
    {
        if (requestModel is null)
        {
            throw new ArgumentNullException(nameof(requestModel));
        }

    using var request = new HttpRequestMessage(HttpMethod.Put, "api/v1.0/configuration/system/observatory")
        {
            Content = CreateJsonContent(requestModel)
        };

        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API observatory update failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

        return await ReadJsonAsync<SystemObservatoryConfigurationResponse>(response, "observatory", cancellationToken).ConfigureAwait(false);
    }

    public async Task<SystemLocalApiConfigurationResponse?> GetSystemLocalApiAsync(CancellationToken cancellationToken)
    {
    using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1.0/configuration/system/local-api");
        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API configuration request failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

        return await ReadJsonAsync<SystemLocalApiConfigurationResponse>(response, "local-api", cancellationToken).ConfigureAwait(false);
    }

    public async Task<SystemLocalApiConfigurationResponse?> UpdateSystemLocalApiAsync(UpdateSystemLocalApiRequest requestModel, CancellationToken cancellationToken)
    {
        if (requestModel is null)
        {
            throw new ArgumentNullException(nameof(requestModel));
        }

    using var request = new HttpRequestMessage(HttpMethod.Put, "api/v1.0/configuration/system/local-api")
        {
            Content = CreateJsonContent(requestModel)
        };

        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API configuration update failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

        return await ReadJsonAsync<SystemLocalApiConfigurationResponse>(response, "local-api", cancellationToken).ConfigureAwait(false);
    }

    public async Task<SystemTelemetryRetentionConfigurationResponse?> GetTelemetryRetentionAsync(CancellationToken cancellationToken)
    {
    using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1.0/configuration/system/telemetry-retention");
        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API telemetry retention request failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

        return await ReadJsonAsync<SystemTelemetryRetentionConfigurationResponse>(response, "telemetry-retention", cancellationToken).ConfigureAwait(false);
    }

    public async Task<SystemTelemetryRetentionConfigurationResponse?> UpdateTelemetryRetentionAsync(UpdateSystemTelemetryRetentionRequest requestModel, CancellationToken cancellationToken)
    {
        if (requestModel is null)
        {
            throw new ArgumentNullException(nameof(requestModel));
        }

    using var request = new HttpRequestMessage(HttpMethod.Put, "api/v1.0/configuration/system/telemetry-retention")
        {
            Content = CreateJsonContent(requestModel)
        };

        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API telemetry retention update failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

        return await ReadJsonAsync<SystemTelemetryRetentionConfigurationResponse>(response, "telemetry-retention", cancellationToken).ConfigureAwait(false);
    }

    public async Task<EquipmentCatalogResponse?> GetEquipmentCatalogAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1.0/configuration/equipment");
        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API equipment catalog request failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

    return await ReadJsonAsync<EquipmentCatalogResponse>(response, "equipment", cancellationToken).ConfigureAwait(false);
    }

    public async Task<CameraDriverCatalogResponse?> GetCameraDriverCatalogAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1.0/configuration/drivers");
        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API camera driver catalog request failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

        return await ReadJsonAsync<CameraDriverCatalogResponse>(response, "camera-drivers", cancellationToken).ConfigureAwait(false);
    }

    public async Task<EquipmentCatalogResponse?> CreateRigAsync(CreateRigRequest requestModel, CancellationToken cancellationToken)
    {
        if (requestModel is null)
        {
            throw new ArgumentNullException(nameof(requestModel));
        }

    using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1.0/configuration/equipment/rigs")
        {
            Content = CreateJsonContent(requestModel)
        };

        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API rig create request failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

        return await ReadJsonAsync<EquipmentCatalogResponse>(response, "equipment", cancellationToken).ConfigureAwait(false);
    }

    public async Task<EquipmentCatalogResponse?> UpdateRigAsync(int rigId, UpdateRigRequest requestModel, CancellationToken cancellationToken)
    {
        if (requestModel is null)
        {
            throw new ArgumentNullException(nameof(requestModel));
        }

    using var request = new HttpRequestMessage(HttpMethod.Put, $"api/v1.0/configuration/equipment/rigs/{rigId}")
        {
            Content = CreateJsonContent(requestModel)
        };

        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API rig update request failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

        return await ReadJsonAsync<EquipmentCatalogResponse>(response, "equipment", cancellationToken).ConfigureAwait(false);
    }

    public async Task<EquipmentCatalogResponse?> DeleteRigAsync(int rigId, long? revision, CancellationToken cancellationToken)
    {
        var uri = revision is { } value
            ? $"api/v1.0/configuration/equipment/rigs/{rigId}?revision={value}"
            : $"api/v1.0/configuration/equipment/rigs/{rigId}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API rig delete request failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

        return await ReadJsonAsync<EquipmentCatalogResponse>(response, "equipment", cancellationToken).ConfigureAwait(false);
    }



    public async Task<EquipmentCatalogResponse?> CreateCameraAsync(CreateCameraRequest requestModel, CancellationToken cancellationToken)
    {
        if (requestModel is null)
        {
            throw new ArgumentNullException(nameof(requestModel));
        }

    using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1.0/configuration/equipment/cameras")
        {
            Content = CreateJsonContent(requestModel)
        };

        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API camera create request failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

    return await ReadJsonAsync<EquipmentCatalogResponse>(response, "equipment", cancellationToken).ConfigureAwait(false);
    }

    public async Task<EquipmentCatalogResponse?> UpdateCameraAsync(int cameraId, UpdateCameraRequest requestModel, CancellationToken cancellationToken)
    {
        if (requestModel is null)
        {
            throw new ArgumentNullException(nameof(requestModel));
        }

    using var request = new HttpRequestMessage(HttpMethod.Put, $"api/v1.0/configuration/equipment/cameras/{cameraId}")
        {
            Content = CreateJsonContent(requestModel)
        };

        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API camera update request failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

    return await ReadJsonAsync<EquipmentCatalogResponse>(response, "equipment", cancellationToken).ConfigureAwait(false);
    }

    public async Task<EquipmentCatalogResponse?> CreateOpticsAsync(CreateOpticsRequest requestModel, CancellationToken cancellationToken)
    {
        if (requestModel is null)
        {
            throw new ArgumentNullException(nameof(requestModel));
        }

    using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1.0/configuration/equipment/optics")
        {
            Content = CreateJsonContent(requestModel)
        };

        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API optics create request failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

        return await ReadJsonAsync<EquipmentCatalogResponse>(response, "equipment", cancellationToken).ConfigureAwait(false);
    }

    public async Task<EquipmentCatalogResponse?> UpdateOpticsAsync(int opticsId, UpdateOpticsRequest requestModel, CancellationToken cancellationToken)
    {
        if (requestModel is null)
        {
            throw new ArgumentNullException(nameof(requestModel));
        }

    using var request = new HttpRequestMessage(HttpMethod.Put, $"api/v1.0/configuration/equipment/optics/{opticsId}")
        {
            Content = CreateJsonContent(requestModel)
        };

        ApplyApiKey(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Local API optics update request failed with status code {StatusCode}.", response.StatusCode);
            return null;
        }

        return await ReadJsonAsync<EquipmentCatalogResponse>(response, "equipment", cancellationToken).ConfigureAwait(false);
    }

    private void ApplyApiKey(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            var headerName = string.IsNullOrWhiteSpace(_options.ApiKeyHeaderName)
                ? "X-Api-Key"
                : _options.ApiKeyHeaderName;

            if (!request.Headers.TryAddWithoutValidation(headerName, _options.ApiKey))
            {
                _logger.LogWarning("Failed to add API key header {HeaderName} to local API request.", headerName);
            }
        }
    }

    private static Guid? ReadGuidHeader(HttpResponseMessage response, string headerName)
    {
        var value = response.Headers.TryGetValues(headerName, out var values)
            ? values.FirstOrDefault()
            : null;

        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    private static DateTimeOffset? ReadDateTimeOffsetHeader(HttpResponseMessage response, string headerName)
    {
        var value = response.Headers.TryGetValues(headerName, out var values)
            ? values.FirstOrDefault()
            : null;

        return DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string? TryResolveExtensionFromFormat(string? format, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(format))
        {
            return format.Equals("png", StringComparison.OrdinalIgnoreCase) ? "png"
                : format.Equals("skimg", StringComparison.OrdinalIgnoreCase) ? "skimg"
                : format.Equals("raw", StringComparison.OrdinalIgnoreCase) ? "raw"
                : format.Equals("fits", StringComparison.OrdinalIgnoreCase) ? "fits"
                : null;
        }

        return TryResolveExtensionFromContentType(contentType);
    }

    private static string? TryResolveExtensionFromContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        return contentType switch
        {
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/jpg" => "jpg",
            "application/vnd.hvo.skia.raw" => "skimg",
            "application/fits" => "fits",
            _ => null
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }

    private static HttpContent CreateJsonContent<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, string resourceName, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Local API {Resource} response could not be deserialized.", resourceName);
            return default;
        }
    }
}

internal static class FrameExportHeaderParser
{
    public static FrameExportImageDescriptor? TryCreateDescriptor(System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        if (headers is null)
        {
            return null;
        }

        try
        {
            var width = ReadIntHeader(headers, "X-HVO-Raw-Width");
            var height = ReadIntHeader(headers, "X-HVO-Raw-Height");
            if (width is null || height is null)
            {
                return null;
            }

            var rowBytes = ReadIntHeader(headers, "X-HVO-Raw-RowBytes") ?? 0;
            var bytesPerPixel = ReadIntHeader(headers, "X-HVO-Raw-BytesPerPixel") ?? 0;
            var colorType = ReadHeader(headers, "X-HVO-Raw-ColorType");
            var alphaType = ReadHeader(headers, "X-HVO-Raw-AlphaType");
            var pixelFormat = ReadHeader(headers, "X-HVO-Raw-PixelFormat");

            var gammaLinear = ReadBoolHeader(headers, "X-HVO-Raw-GammaLinear");
            var isSrgb = ReadBoolHeader(headers, "X-HVO-Raw-IsSrgb");
            var hasNumericTransfer = ReadBoolHeader(headers, "X-HVO-Raw-TransferNumeric");
            var colorSpace = ReadHeader(headers, "X-HVO-Raw-ColorSpace");

            return new FrameExportImageDescriptor(
                width.Value,
                height.Value,
                rowBytes,
                bytesPerPixel,
                colorType ?? pixelFormat ?? string.Empty,
                alphaType ?? string.Empty,
                gammaLinear,
                isSrgb,
                hasNumericTransfer,
                colorSpace);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadHeader(System.Net.Http.Headers.HttpResponseHeaders headers, string name)
        => headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static int? ReadIntHeader(System.Net.Http.Headers.HttpResponseHeaders headers, string name)
        => int.TryParse(ReadHeader(headers, name), out var value) ? value : null;

    private static bool ReadBoolHeader(System.Net.Http.Headers.HttpResponseHeaders headers, string name)
        => bool.TryParse(ReadHeader(headers, name), out var value) && value;
}
