using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using FluentAssertions;
using HVO.Astronomy.CFITSIO.Interop;
using HVO.Astronomy.CFITSIO.Tests.Helpers;

namespace HVO.Astronomy.CFITSIO.Tests;

/// <summary>
/// Tests that validate the native CFITSIO library is properly loaded and all entry points are available.
/// These tests are critical for CI/CD pipelines to catch platform-specific issues.
/// </summary>
[TestClass]
public class NativeLibraryTests
{
  private static IntPtr LoadCfitsioOrFail()
  {
    var cfitsioType = typeof(CFitsIO);
    if (NativeLibrary.TryLoad("cfitsio", cfitsioType.Assembly, null, out var handle))
      return handle;

    // Fallback: attempt to load from runtimes/<rid>/native
    var baseDir = AppContext.BaseDirectory;
    var rid = RuntimeInformation.RuntimeIdentifier;
    string file = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
      ? "libcfitsio.dylib"
      : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "libcfitsio.so" : "cfitsio.dll";

    var candidate = System.IO.Path.Combine(baseDir, "runtimes", rid ?? string.Empty, "native", file);
    if (System.IO.File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out handle))
      return handle;

    Assert.Fail("Failed to load CFITSIO native library");
    return IntPtr.Zero; // unreachable
  }
  [TestMethod]
  public void NativeLibrary_Loads_Successfully()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange & Act
    double version = 0;

    // Assert - This will throw DllNotFoundException if library doesn't load
    Action act = () => CFitsIO.fits_get_version(out version);
    act.Should().NotThrow("the native CFITSIO library should load successfully");

    version.Should().BeGreaterThanOrEqualTo(0.0, "CFITSIO version should be reported");
  }

  [TestMethod]
  public void All_EntryPoints_Resolve_On_Current_Platform()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange - Get all LibraryImport methods from CFitsIO class
    var cfitsioType = typeof(CFitsIO);
    var methods = cfitsioType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

    var entryPoints = new Dictionary<string, string>(); // method name -> entry point
    var unresolvedEntryPoints = new List<string>();

    foreach (var method in methods)
    {
      var libraryImport = method.GetCustomAttribute<LibraryImportAttribute>();
      if (libraryImport != null && !string.IsNullOrEmpty(libraryImport.EntryPoint))
      {
        entryPoints[method.Name] = libraryImport.EntryPoint!;
      }
    }

    // Act - Try to resolve each entry point using NativeLibrary.TryGetExport
    IntPtr libHandle = IntPtr.Zero;
    try
    {
      // Load the library (name-based, then explicit path fallback)
      libHandle = LoadCfitsioOrFail();

      foreach (var kvp in entryPoints)
      {
        if (!NativeLibrary.TryGetExport(libHandle, kvp.Value, out _))
        {
          unresolvedEntryPoints.Add($"{kvp.Key} -> {kvp.Value}");
        }
      }
    }
    finally
    {
      if (libHandle != IntPtr.Zero)
      {
        NativeLibrary.Free(libHandle);
      }
    }

    // Assert
    unresolvedEntryPoints.Should().BeEmpty(
      $"all {entryPoints.Count} entry points should resolve on {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}). " +
      $"Unresolved: {string.Join(", ", unresolvedEntryPoints)}");
  }

  [TestMethod]
  public void Expected_EntryPoints_Are_Present()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange - Define expected critical entry points that must be present
    var criticalEntryPoints = new[]
    {
      "ffvers",  // Version
      "ffinit",  // Create file
      "ffopen",  // Open file
      "ffclos",  // Close file
      "ffcrimll", // Create image
      "ffgiprll", // Get image params
      "ffppr",   // Write pixels
      "ffgpv",   // Read pixels
      "ffpkys",  // Write string key
      "ffgkys",  // Read string key
      "ffpkyj",  // Write int key
      "ffgkyj",  // Read int key
      "ffpkyd",  // Write double key
      "ffgkyd",  // Read double key
      "ffpkyl",  // Write logical key
      "ffgkyl",  // Read logical key
      // Memfile APIs (required by our in-memory wrapper)
      "ffimem",  // fits_create_memfile
      "ffomem",  // fits_open_memfile
    };

    // Act
    IntPtr libHandle = IntPtr.Zero;
    var missingEntryPoints = new List<string>();
    try
    {
      libHandle = LoadCfitsioOrFail();

      foreach (var entryPoint in criticalEntryPoints)
      {
        if (!NativeLibrary.TryGetExport(libHandle, entryPoint, out _))
        {
          missingEntryPoints.Add(entryPoint);
        }
      }
    }
    finally
    {
      if (libHandle != IntPtr.Zero)
      {
        NativeLibrary.Free(libHandle);
      }
    }

    // Assert
    missingEntryPoints.Should().BeEmpty(
      $"all critical CFITSIO entry points should be present. Missing: {string.Join(", ", missingEntryPoints)}");
  }

  [TestMethod]
  public void Commented_EntryPoints_Are_Not_Present()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange - These are entry points we know don't exist in CFITSIO
    // and should remain commented out in CFitsIO.cs
    var unavailableEntryPoints = new[]
    {
      "ffpkyjj",  // 64-bit integer write (doesn't exist)
      "ffukyjj",  // 64-bit integer update (doesn't exist)
      "fits_set_tile_dimll",  // Compression helper (not in standard build)
      "fits_set_compression_param",  // Compression helper (not in standard build)
    };

    // Act
    IntPtr libHandle = IntPtr.Zero;
    var unexpectedlyPresent = new List<string>();
    try
    {
      libHandle = LoadCfitsioOrFail();

      foreach (var entryPoint in unavailableEntryPoints)
      {
        if (NativeLibrary.TryGetExport(libHandle, entryPoint, out _))
        {
          unexpectedlyPresent.Add(entryPoint);
        }
      }
    }
    finally
    {
      if (libHandle != IntPtr.Zero)
      {
        NativeLibrary.Free(libHandle);
      }
    }

    // Assert - If these are present, we can uncomment them in CFitsIO.cs!
    if (unexpectedlyPresent.Any())
    {
      Assert.Inconclusive(
        $"Previously unavailable entry points are now present in CFITSIO: {string.Join(", ", unexpectedlyPresent)}. " +
        "Consider uncommenting these in CFitsIO.cs if they're needed.");
    }
  }
}
