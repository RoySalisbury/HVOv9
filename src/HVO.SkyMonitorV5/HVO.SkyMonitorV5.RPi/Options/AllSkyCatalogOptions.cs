#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Root configuration section for reusable camera, lens, and rig catalog entries.
/// </summary>
public sealed class AllSkyCatalogOptions : IValidatableObject
{
    public const string SectionName = "AllSkyCatalogs";

    public IList<CameraCatalogEntryOptions> Cameras { get; set; } = new List<CameraCatalogEntryOptions>();

    public IList<LensCatalogEntryOptions> Lenses { get; set; } = new List<LensCatalogEntryOptions>();

    public RigCatalogOptions Rigs { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var validation in ValidateUniqueNames(Cameras, static c => c.Name, nameof(Cameras)))
        {
            yield return validation;
        }

        foreach (var validation in ValidateUniqueNames(Lenses, static l => l.Name, nameof(Lenses)))
        {
            yield return validation;
        }

        if (Rigs is null)
        {
            yield return new ValidationResult("Rig catalog configuration is required.", new[] { nameof(Rigs) });
        }
        else
        {
            foreach (var validation in Rigs.Validate(new ValidationContext(Rigs)))
            {
                yield return validation;
            }
        }
    }

    internal static IEnumerable<ValidationResult> ValidateUniqueNames<T>(IEnumerable<T> items, Func<T, string?> getName, string collectionName)
    {
        if (items is null)
        {
            yield break;
        }

        var index = 0;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var name = getName(item);
            if (string.IsNullOrWhiteSpace(name))
            {
                yield return new ValidationResult("Catalog entries must have a name.", new[] { $"{collectionName}[{index}].Name" });
            }
            else if (!set.Add(name.Trim()))
            {
                yield return new ValidationResult($"Duplicate catalog entry '{name}' detected.", new[] { $"{collectionName}[{index}].Name" });
            }

            if (item is IValidatableObject validatable)
            {
                foreach (var validation in validatable.Validate(new ValidationContext(item)))
                {
                    yield return validation;
                }
            }

            index++;
        }
    }
}

public sealed class CameraCatalogEntryOptions : IValidatableObject
{
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public SensorSpecOptions Sensor { get; set; } = new();

    public CameraCapabilitiesOptions Capabilities { get; set; } = new();

    [Required]
    public CameraDescriptorOptions Descriptor { get; set; } = new();

    [EnumDataType(typeof(CameraDriverId))]
    public CameraDriverId DriverId { get; set; } = CameraDriverId.Unknown;

    [MaxLength(128)]
    public string? DriverIdentifier { get; set; }

    public bool? IsSynthetic { get; set; }

    [MaxLength(128)]
    public string? SyntheticProfile { get; set; }

    public string? DriverSettingsJson { get; set; }

    public CameraSpec ToCameraSpec()
    {
        var sensor = Sensor?.ToSensorSpec() ?? throw new InvalidOperationException($"Camera '{Name}' is missing sensor configuration.");
        var capabilities = Capabilities?.ToCameraCapabilities() ?? CameraCapabilities.Empty;
        var descriptor = Descriptor?.ToCameraDescriptor() ?? throw new InvalidOperationException($"Camera '{Name}' is missing descriptor metadata.");
        var driverId = DriverId;
        var synthetic = IsSynthetic ?? driverId == CameraDriverId.Synthetic;
        var profile = string.IsNullOrWhiteSpace(SyntheticProfile) ? null : SyntheticProfile.Trim();
        var driverIdentifier = string.IsNullOrWhiteSpace(DriverIdentifier) ? null : DriverIdentifier.Trim();
        var driverSettings = string.IsNullOrWhiteSpace(DriverSettingsJson) ? null : DriverSettingsJson.Trim();

        if (synthetic && driverId != CameraDriverId.Synthetic)
        {
            driverId = CameraDriverId.Synthetic;
        }

        if (!synthetic)
        {
            profile = null;
        }

        var spec = new CameraSpec(Name.Trim(), sensor, capabilities, descriptor, driverId, synthetic, profile, driverSettings);

        return string.IsNullOrWhiteSpace(driverIdentifier)
            ? spec
            : spec with { DriverIdentifierOverride = driverIdentifier };
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Sensor is null)
        {
            yield return new ValidationResult("Camera sensor configuration is required.", new[] { nameof(Sensor) });
        }
        else
        {
            ValidationResult? sensorError = null;
            try
            {
                _ = Sensor.ToSensorSpec();
            }
            catch (InvalidOperationException ex)
            {
                sensorError = new ValidationResult(ex.Message, new[] { nameof(Sensor) });
            }

            if (sensorError is not null)
            {
                yield return sensorError;
            }
        }

        if (Capabilities is null)
        {
            yield return new ValidationResult("Camera capabilities configuration is required.", new[] { nameof(Capabilities) });
        }
        else
        {
            foreach (var validation in Capabilities.Validate(nameof(Capabilities)))
            {
                yield return validation;
            }
        }

        if (Descriptor is null)
        {
            yield return new ValidationResult("Camera descriptor configuration is required.", new[] { nameof(Descriptor) });
        }
        else
        {
            foreach (var validation in Descriptor.Validate(nameof(Descriptor)))
            {
                yield return validation;
            }
        }

        if (DriverId == CameraDriverId.Unknown)
        {
            yield return new ValidationResult(
                "Camera catalog entries must specify a driver identifier.",
                new[] { nameof(DriverId) });
        }

        if (((IsSynthetic ?? false) || DriverId == CameraDriverId.Synthetic) && string.IsNullOrWhiteSpace(SyntheticProfile))
        {
            yield return new ValidationResult(
                "Synthetic cameras must provide a synthetic profile identifier.",
                new[] { nameof(SyntheticProfile) });
        }

        if (!string.IsNullOrWhiteSpace(DriverIdentifier) && DriverIdentifier.Length > 128)
        {
            yield return new ValidationResult(
                "Driver identifier overrides must be 128 characters or fewer.",
                new[] { nameof(DriverIdentifier) });
        }

        if (!string.IsNullOrWhiteSpace(DriverSettingsJson))
        {
            var isValid = true;
            try
            {
                using var _ = JsonDocument.Parse(DriverSettingsJson);
            }
            catch (JsonException)
            {
                isValid = false;
            }

            if (!isValid)
            {
                yield return new ValidationResult(
                    "Driver settings JSON is invalid.",
                    new[] { nameof(DriverSettingsJson) });
            }
        }
    }
}

public sealed class LensCatalogEntryOptions : IValidatableObject
{
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public LensSpecOptions Lens { get; set; } = new();

    public LensSpec ToLensSpec() => Lens?.ToLensSpec() ?? throw new InvalidOperationException($"Lens '{Name}' configuration is missing.");

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Lens is null)
        {
            yield return new ValidationResult("Lens configuration is required.", new[] { nameof(Lens) });
            yield break;
        }

        ValidationResult? lensError = null;
        try
        {
            _ = Lens.ToLensSpec();
        }
        catch (InvalidOperationException ex)
        {
            lensError = new ValidationResult(ex.Message, new[] { nameof(Lens) });
        }

        if (lensError is not null)
        {
            yield return lensError;
        }
    }
}

public sealed class RigCatalogOptions : IValidatableObject
{
    [MaxLength(128)]
    public string ActiveRig { get; set; } = string.Empty;

    public IList<RigCatalogEntryOptions> Entries { get; set; } = new List<RigCatalogEntryOptions>();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Entries is null)
        {
            yield return new ValidationResult("Rig catalog entries collection cannot be null.", new[] { nameof(Entries) });
            yield break;
        }

        if (Entries.Count > 0)
        {
            foreach (var validation in AllSkyCatalogOptions.ValidateUniqueNames(Entries, static e => e.Name, nameof(Entries)))
            {
                yield return validation;
            }

            if (!string.IsNullOrWhiteSpace(ActiveRig) && Entries.All(e => !string.Equals(e.Name, ActiveRig, StringComparison.OrdinalIgnoreCase)))
            {
                yield return new ValidationResult($"Active rig '{ActiveRig}' was not found in the catalog entries.", new[] { nameof(ActiveRig) });
            }
        }
        else if (!string.IsNullOrWhiteSpace(ActiveRig))
        {
            yield return new ValidationResult($"Active rig '{ActiveRig}' was specified but no rig catalog entries were provided.", new[] { nameof(ActiveRig) });
        }
    }
}

public sealed class RigCatalogEntryOptions : IValidatableObject
{
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Camera { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Lens { get; set; } = string.Empty;

    [Range(0.0, 90.0)]
    public double BoresightAltDeg { get; set; } = 90.0;

    [Range(0.0, 360.0)]
    public double BoresightAzDeg { get; set; } = 0.0;

    public RigSpec ToRigSpec(CameraSpec camera, LensSpec lens)
    {
        var rigName = string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
        return new(rigName.Trim(), camera, lens, BoresightAltDeg, BoresightAzDeg);
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Camera))
        {
            yield return new ValidationResult("Rig must reference a camera entry.", new[] { nameof(Camera) });
        }

        if (string.IsNullOrWhiteSpace(Lens))
        {
            yield return new ValidationResult("Rig must reference a lens entry.", new[] { nameof(Lens) });
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            yield return new ValidationResult("Rig display name is required.", new[] { nameof(DisplayName) });
        }
    }
}
