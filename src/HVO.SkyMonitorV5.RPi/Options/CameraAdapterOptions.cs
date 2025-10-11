using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using HVO.SkyMonitorV5.RPi.Catalog;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Models;
using System.Linq;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configuration for each camera adapter instance registered in the host.
/// </summary>
public sealed class CameraAdapterOptions : IValidatableObject
{
    public const string SectionName = "AllSkyCameras";

    [Required]
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Adapter { get; set; } = CameraAdapterTypes.Mock;

    [MaxLength(128)]
    public string? RigCatalog { get; set; }

    public RigSpecOptions? Rig { get; set; }

    public RigSpec ResolveRig(IRigCatalog rigCatalog)
    {
        if (rigCatalog is null)
        {
            throw new ArgumentNullException(nameof(rigCatalog));
        }

        if (!string.IsNullOrWhiteSpace(RigCatalog))
        {
            var reference = RigCatalog!.Trim();
            var result = rigCatalog.Resolve(reference);
            if (result.IsSuccessful)
            {
                return result.Value;
            }

            var error = result.Error ?? new InvalidOperationException($"Rig catalog entry '{reference}' could not be resolved.");
            throw new InvalidOperationException($"Rig catalog entry '{reference}' could not be resolved for camera '{Name}'.", error);
        }

        if (Rig is not null)
        {
            return Rig.ToRigSpec();
        }

        throw new InvalidOperationException($"Camera '{Name}' must specify either '{nameof(RigCatalog)}' or inline '{nameof(Rig)}' configuration.");
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(RigCatalog) && Rig is null)
        {
            yield return new ValidationResult(
                $"Camera '{Name}' must specify either a rig catalog reference or inline rig configuration.",
                new[] { nameof(RigCatalog), nameof(Rig) });
        }

        if (!string.IsNullOrWhiteSpace(RigCatalog) && Rig is not null)
        {
            yield return new ValidationResult(
                "Specify either a rig catalog reference or inline rig configuration, not both.",
                new[] { nameof(RigCatalog), nameof(Rig) });
        }

        if (Rig is not null && string.IsNullOrWhiteSpace(RigCatalog))
        {
            foreach (var result in Rig.Validate(new ValidationContext(Rig)))
            {
                yield return result;
            }
        }
    }
}

public static class CameraAdapterTypes
{
    public const string Mock = "Mock";
    public const string MockFisheye = "MockFisheye"; // legacy alias
    public const string MockColor = "MockColor";
    public const string MockColorFisheye = "MockColorFisheye";
    public const string Zwo = "Zwo";

    public static bool IsMock(string adapter)
        => adapter.Equals(Mock, StringComparison.OrdinalIgnoreCase)
            || adapter.Equals(MockFisheye, StringComparison.OrdinalIgnoreCase);

    public static bool IsMockColor(string adapter)
        => adapter.Equals(MockColor, StringComparison.OrdinalIgnoreCase)
            || adapter.Equals(MockColorFisheye, StringComparison.OrdinalIgnoreCase);

    public static bool IsZwo(string adapter)
        => adapter.Equals(Zwo, StringComparison.OrdinalIgnoreCase);
}

public sealed class RigSpecOptions : IValidatableObject
{
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public SensorSpecOptions Sensor { get; set; } = new();

    [Required]
    public LensSpecOptions Lens { get; set; } = new();

    public CameraDescriptorOptions? Descriptor { get; set; }

    public CameraSpecOptions? Camera { get; set; }

    [Range(0.0, 90.0)]
    public double? BoresightAltDeg { get; set; }

    [Range(0.0, 360.0)]
    public double? BoresightAzDeg { get; set; }

    public RigSpec ToRigSpec()
    {
        var sensorSpec = Sensor.ToSensorSpec();
        var cameraOptions = Camera;
        var descriptor = Descriptor?.ToCameraDescriptor();

        var resolvedCameraName = !string.IsNullOrWhiteSpace(cameraOptions?.Name)
            ? cameraOptions!.Name!.Trim()
            : !string.IsNullOrWhiteSpace(descriptor?.Model)
                ? descriptor!.Model!
                : Name;

        var capabilities = cameraOptions?.Capabilities?.ToCameraCapabilities() ?? CameraCapabilities.Empty;
        var cameraSpec = descriptor is not null
            ? new CameraSpec(resolvedCameraName, sensorSpec, capabilities, descriptor)
            : new CameraSpec(resolvedCameraName, sensorSpec, capabilities);

        return new RigSpec(
            Name,
            cameraSpec,
            Lens.ToLensSpec(),
            BoresightAltDeg ?? 90.0,
            BoresightAzDeg ?? 0.0);
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult("Rig name is required.", new[] { nameof(Name) });
        }

        if (Sensor is null)
        {
            yield return new ValidationResult("Sensor configuration is required.", new[] { nameof(Sensor) });
        }

        if (Lens is null)
        {
            yield return new ValidationResult("Lens configuration is required.", new[] { nameof(Lens) });
        }

        if (Camera is not null)
        {
            foreach (var result in Camera.Validate(nameof(Camera)))
            {
                yield return result;
            }
        }

        if (Descriptor is not null)
        {
            foreach (var result in Descriptor.Validate(nameof(Descriptor)))
            {
                yield return result;
            }
        }
    }
}

public sealed class SensorSpecOptions
{
    [Range(1, 16_384)]
    public int WidthPx { get; set; }

    [Range(1, 16_384)]
    public int HeightPx { get; set; }

    [Range(0.1, 50.0)]
    public double PixelSizeMicrons { get; set; }

    public double? CxPx { get; set; }

    public double? CyPx { get; set; }

    public SensorSpec ToSensorSpec()
    {
        if (WidthPx <= 0 || HeightPx <= 0)
        {
            throw new InvalidOperationException("Sensor dimensions must be positive.");
        }

        if (PixelSizeMicrons <= 0)
        {
            throw new InvalidOperationException("Sensor pixel size must be positive.");
        }

        return new SensorSpec(WidthPx, HeightPx, PixelSizeMicrons, CxPx, CyPx);
    }
}

public sealed class CameraSpecOptions
{
    [MaxLength(128)]
    public string? Name { get; set; }

    public CameraCapabilitiesOptions Capabilities { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(string memberName)
    {
        if (Capabilities is null)
        {
            yield return new ValidationResult(
                "Camera capabilities configuration is required when specifying a camera.",
                new[] { memberName });
            yield break;
        }

        foreach (var result in Capabilities.Validate($"{memberName}.{nameof(Capabilities)}"))
        {
            yield return result;
        }
    }
}

public sealed class CameraCapabilitiesOptions
{
    [EnumDataType(typeof(CameraColorMode))]
    public CameraColorMode ColorMode { get; set; } = CameraColorMode.Unknown;

    [EnumDataType(typeof(CameraSensorTechnology))]
    public CameraSensorTechnology SensorTechnology { get; set; } = CameraSensorTechnology.Unknown;

    [EnumDataType(typeof(CameraBodyType))]
    public CameraBodyType BodyType { get; set; } = CameraBodyType.Unknown;

    [EnumDataType(typeof(CameraCoolingType))]
    public CameraCoolingType Cooling { get; set; } = CameraCoolingType.None;

    public bool SupportsGainControl { get; set; } = true;

    public bool SupportsExposureControl { get; set; } = true;

    public bool SupportsTemperatureTelemetry { get; set; }

    public bool SupportsSoftwareBinning { get; set; } = true;

    public string[] AdditionalTags { get; set; } = Array.Empty<string>();

    public CameraCapabilities ToCameraCapabilities() => new(
        ColorMode,
        SensorTechnology,
        BodyType,
        Cooling,
        SupportsGainControl,
        SupportsExposureControl,
        SupportsTemperatureTelemetry,
        SupportsSoftwareBinning,
        AdditionalTags);

    public IEnumerable<ValidationResult> Validate(string memberName)
    {
        if (AdditionalTags is { Length: > 0 })
        {
            for (var i = 0; i < AdditionalTags.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(AdditionalTags[i]))
                {
                    yield return new ValidationResult(
                        "Additional capability tags cannot be blank.",
                        new[] { $"{memberName}.{nameof(AdditionalTags)}[{i}]" });
                }
            }
        }
    }
}

public sealed class LensSpecOptions
{
    [EnumDataType(typeof(ProjectionModel))]
    public ProjectionModel Model { get; set; } = ProjectionModel.Equidistant;

    [Range(0.1, 2_000.0)]
    public double FocalLengthMm { get; set; }

    [Range(0.0, 360.0)]
    public double FovXDeg { get; set; }

    [Range(0.0, 360.0)]
    public double? FovYDeg { get; set; }

    [Range(-180.0, 180.0)]
    public double RollDeg { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [EnumDataType(typeof(LensKind))]
    public LensKind Kind { get; set; } = LensKind.Rectilinear;

    public LensSpec ToLensSpec()
    {
        if (FocalLengthMm <= 0)
        {
            throw new InvalidOperationException("Lens focal length must be positive.");
        }

        return new LensSpec(
            Model,
            FocalLengthMm,
            FovXDeg,
            FovYDeg,
            RollDeg,
            Name,
            Kind);
    }
}

public sealed class CameraDescriptorOptions
{
    [Required]
    [MaxLength(128)]
    public string Manufacturer { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Model { get; set; } = string.Empty;

    [MaxLength(64)]
    public string DriverVersion { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string AdapterName { get; set; } = string.Empty;

    public string[] Capabilities { get; set; } = Array.Empty<string>();

    public CameraDescriptor ToCameraDescriptor()
    {
        if (string.IsNullOrWhiteSpace(Manufacturer))
        {
            throw new InvalidOperationException("Camera descriptor must include a manufacturer.");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new InvalidOperationException("Camera descriptor must include a model.");
        }

        if (string.IsNullOrWhiteSpace(AdapterName))
        {
            throw new InvalidOperationException("Camera descriptor must include an adapter name.");
        }

        IReadOnlyCollection<string> descriptorCapabilities = Capabilities?.Length > 0
            ? Capabilities
                .Where(static c => !string.IsNullOrWhiteSpace(c))
                .Select(static c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

        return new CameraDescriptor(
            Manufacturer.Trim(),
            Model.Trim(),
            DriverVersion?.Trim() ?? string.Empty,
            AdapterName.Trim(),
            descriptorCapabilities);
    }

    public IEnumerable<ValidationResult> Validate(string parentProperty)
    {
        if (string.IsNullOrWhiteSpace(Manufacturer))
        {
            yield return new ValidationResult("Manufacturer is required when descriptor is provided.", new[] { parentProperty + "." + nameof(Manufacturer) });
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            yield return new ValidationResult("Model is required when descriptor is provided.", new[] { parentProperty + "." + nameof(Model) });
        }

        if (string.IsNullOrWhiteSpace(AdapterName))
        {
            yield return new ValidationResult("AdapterName is required when descriptor is provided.", new[] { parentProperty + "." + nameof(AdapterName) });
        }
    }
}
