using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Models;
using System.Linq;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configuration for each camera adapter instance registered in the host.
/// </summary>
public sealed class CameraAdapterOptions
{
    public const string SectionName = "AllSkyCameras";

    [Required]
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Adapter { get; set; } = CameraAdapterTypes.Mock;

    [Required]
    public RigSpecOptions Rig { get; set; } = new();
}

public static class CameraAdapterTypes
{
    public const string Mock = "Mock";
    public const string MockFisheye = "MockFisheye"; // legacy alias
    public const string Zwo = "Zwo";

    public static bool IsMock(string adapter)
        => adapter.Equals(Mock, StringComparison.OrdinalIgnoreCase)
            || adapter.Equals(MockFisheye, StringComparison.OrdinalIgnoreCase);

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

    public RigSpec ToRigSpec() => new(
        Name,
        Sensor.ToSensorSpec(),
        Lens.ToLensSpec(),
        Descriptor?.ToCameraDescriptor());

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
