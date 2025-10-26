using System;
using System.Text.Json;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Cameras.Drivers;

/// <summary>
/// Helper methods for binding camera driver settings payloads to strongly typed configuration objects.
/// </summary>
public static class CameraDriverSettingsHelper
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Resolves the settings payload declared by <paramref name="camera"/> against the registered driver metadata.
    /// </summary>
    /// <param name="camera">Camera specification supplying the raw JSON payload.</param>
    /// <param name="registry">Registry used to look up driver descriptors.</param>
    /// <param name="logger">Optional logger for diagnostic warnings.</param>
    /// <returns>The resolved payload result.</returns>
    public static Result<CameraDriverSettingsPayload> Resolve(CameraSpec camera, ICameraDriverRegistry registry, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(registry);

        var driverId = camera.DriverIdentifier;
        CameraDriverDescriptor? descriptor = null;

        if (!string.IsNullOrWhiteSpace(driverId) && registry.TryGetDriver(driverId, out var registered))
        {
            descriptor = registered;
        }
        else if (!string.IsNullOrWhiteSpace(driverId) && !string.IsNullOrWhiteSpace(camera.DriverSettingsJson))
        {
            logger?.LogWarning(
                "Camera driver settings for {DriverId} could not be bound because the driver is not registered.",
                driverId);
        }

        var resolvedDriverId = descriptor?.Id ?? (!string.IsNullOrWhiteSpace(driverId) ? driverId : null);
        return Parse(camera.DriverSettingsJson, resolvedDriverId, descriptor, descriptor?.ConfigurationType);
    }

    /// <summary>
    /// Resolves the settings payload for the specified descriptor.
    /// </summary>
    /// <param name="json">Raw JSON payload supplied by the caller.</param>
    /// <param name="descriptor">Descriptor describing the driver.</param>
    /// <returns>The resolved payload result.</returns>
    public static Result<CameraDriverSettingsPayload> Resolve(string? json, CameraDriverDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Parse(json, descriptor.Id, descriptor, descriptor.ConfigurationType);
    }

    /// <summary>
    /// Resolves the settings payload without any driver metadata context.
    /// </summary>
    /// <param name="json">Raw JSON payload supplied by the caller.</param>
    /// <returns>The resolved payload result.</returns>
    public static Result<CameraDriverSettingsPayload> Resolve(string? json)
        => Parse(json, null, null, null);

    private static Result<CameraDriverSettingsPayload> Parse(
        string? json,
        string? driverId,
        CameraDriverDescriptor? descriptor,
        Type? configurationType)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result<CameraDriverSettingsPayload>.Success(
                new CameraDriverSettingsPayload(driverId, descriptor, null, default));
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var element = document.RootElement.Clone();

            if (configurationType is null)
            {
                return Result<CameraDriverSettingsPayload>.Success(
                    new CameraDriverSettingsPayload(driverId, descriptor, null, element));
            }

            var configuration = JsonSerializer.Deserialize(element, configurationType, SerializerOptions);
            return Result<CameraDriverSettingsPayload>.Success(
                new CameraDriverSettingsPayload(driverId, descriptor, configuration, element));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var identifier = driverId ?? descriptor?.Id ?? "Unknown";
            return Result<CameraDriverSettingsPayload>.Failure(
                new InvalidOperationException($"Driver settings JSON could not be parsed for '{identifier}'.", ex));
        }
    }
}

/// <summary>
/// Represents a resolved camera driver settings payload with optional typed configuration.
/// </summary>
/// <param name="DriverId">Identifier supplied by the camera spec or registry.</param>
/// <param name="Descriptor">Descriptor for the matched driver when available.</param>
/// <param name="Configuration">Typed configuration object if deserialization succeeded.</param>
/// <param name="RawJson">Raw JSON payload preserved for fall-back handling.</param>
public sealed record CameraDriverSettingsPayload(
    string? DriverId,
    CameraDriverDescriptor? Descriptor,
    object? Configuration,
    JsonElement RawJson)
{
    /// <summary>Indicates the payload includes a typed configuration object.</summary>
    public bool HasTypedConfiguration => Configuration is not null;

    /// <summary>Indicates raw JSON is present even if no typed configuration is available.</summary>
    public bool HasRawJson => RawJson.ValueKind != JsonValueKind.Undefined;
}
