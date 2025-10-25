using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HVO.Astronomy.CFITSIO.Interop;

static class VersionProbe
{
  static int Main(string[] args)
  {
    Console.WriteLine("CFITSIO Version Probe\n======================");
    Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
    Console.WriteLine($"RID-like: {RuntimeInformation.RuntimeIdentifier}");
    Console.WriteLine($"Process Architecture: {RuntimeInformation.ProcessArchitecture}");
    Console.WriteLine($"Base Directory: {AppContext.BaseDirectory}");

    // List any runtimes/* native assets present in the output directory
    var runtimesDir = Path.Combine(AppContext.BaseDirectory, "runtimes");
    if (Directory.Exists(runtimesDir))
    {
      Console.WriteLine($"\nFound runtimes directory: {runtimesDir}");
      foreach (var dir in Directory.EnumerateDirectories(runtimesDir))
      {
        Console.WriteLine($"- {dir}");
        foreach (var nativeDir in Directory.EnumerateDirectories(dir, "native", SearchOption.AllDirectories))
        {
          foreach (var f in Directory.EnumerateFiles(nativeDir))
          {
            Console.WriteLine($"  - {f}");
          }
        }
      }
    }
    else
    {
      Console.WriteLine("\nNo runtimes directory present in output.");
    }

    // Validate that all managed entry points resolve to exports in the native library
    ValidateEntryPoints();

    // Try to get CFITSIO version via managed wrapper
    try
    {
      CFitsIO.fits_get_version(out double version);
      Console.WriteLine($"\nCFITSIO managed wrapper loaded. Version: {version:0.000}");
      return 0;
    }
    catch (DllNotFoundException ex)
    {
      Console.WriteLine("\nERROR: Native library not found (DllNotFoundException)");
      Console.WriteLine(ex.ToString());
      PrintResolutionHints();
      return 2;
    }
    catch (BadImageFormatException ex)
    {
      Console.WriteLine("\nERROR: Native library incompatible (BadImageFormatException)\nLikely architecture mismatch.");
      Console.WriteLine(ex.ToString());
      PrintResolutionHints();
      return 3;
    }
    catch (Exception ex)
    {
      Console.WriteLine("\nERROR: Unexpected exception while loading/using CFITSIO.");
      Console.WriteLine(ex.ToString());
      PrintResolutionHints();
      // Extra: attempt manual symbol discovery
      TryManualSymbolProbe();
      return 1;
    }
  }

  static void PrintResolutionHints()
  {
    Console.WriteLine("\nHints:");
    Console.WriteLine("- Verify the package contains a native asset for your RID (e.g., runtimes/linux-x64/native/libcfitsio.so)");
    Console.WriteLine("- Check that the file is copied to the output folder under runtimes/<rid>/native");
    Console.WriteLine("- Ensure the native dependencies (zlib, bzip2, curl, SSL) are available on this system if dynamically linked");
    Console.WriteLine("- Confirm process arch matches native binary arch (e.g., x64 vs arm64)");
  }

  static void TryManualSymbolProbe()
  {
    try
    {
      // Attempt to load from runtimes/<rid>/native
      var rid = RuntimeInformation.RuntimeIdentifier;
      var probe = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native");
      if (!Directory.Exists(probe))
      {
        // common fallback RID folder names
        string[] candidates = new[] { "linux-arm64", "linux-x64", "osx-arm64", "osx-x64" };
        probe = candidates.Select(c => Path.Combine(AppContext.BaseDirectory, "runtimes", c, "native"))
                          .FirstOrDefault(Directory.Exists) ?? probe;
      }

      var libName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libcfitsio.dylib" : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cfitsio.dll" : "libcfitsio.so";
      var libPath = Path.Combine(probe, libName);
      Console.WriteLine($"\nManual probe path: {libPath}");
      if (!File.Exists(libPath)) { Console.WriteLine("Manual probe: library file not found."); return; }

      if (!NativeLibrary.TryLoad(libPath, out var handle)) { Console.WriteLine("Manual probe: NativeLibrary.TryLoad failed."); return; }
      try
      {
        bool v1 = NativeLibrary.TryGetExport(handle, "ffvers", out var sym1);
        bool v2 = NativeLibrary.TryGetExport(handle, "fits_get_version", out var sym2);
        Console.WriteLine($"Exports: ffvers={(v1 ? "found" : "missing")}, fits_get_version={(v2 ? "found" : "missing")}");
      }
      finally
      {
        NativeLibrary.Free(handle);
      }
    }
    catch (Exception e)
    {
      Console.WriteLine($"Manual symbol probe failed: {e}");
    }
  }

  // Validate all LibraryImport entry points exposed by the managed wrapper against the native library
  static void ValidateEntryPoints()
  {
    try
    {
      var rid = RuntimeInformation.RuntimeIdentifier;
      var baseDir = AppContext.BaseDirectory;
      var probeDirs = new[]
      {
        Path.Combine(baseDir, "runtimes", rid, "native"),
        Path.Combine(baseDir, "runtimes", "linux-arm64", "native"),
        Path.Combine(baseDir, "runtimes", "linux-x64", "native"),
        Path.Combine(baseDir, "runtimes", "osx-arm64", "native"),
        Path.Combine(baseDir, "runtimes", "osx-x64", "native")
      };
      var libName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libcfitsio.dylib" : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cfitsio.dll" : "libcfitsio.so";
      var libPath = probeDirs.FirstOrDefault(d => Directory.Exists(d) && File.Exists(Path.Combine(d, libName)));
      if (libPath is null)
      {
        Console.WriteLine("\nEntry point validation skipped: native library not found in runtimes/*/native.");
        return;
      }
      var fullLibPath = Path.Combine(libPath, libName);
      Console.WriteLine($"\nValidating entry points against: {fullLibPath}");

      if (!NativeLibrary.TryLoad(fullLibPath, out var handle))
      {
        Console.WriteLine("Failed to load native library for validation.");
        return;
      }

      try
      {
        var t = typeof(CFitsIO);
        var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var entries = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var m in methods)
        {
          foreach (var cad in m.GetCustomAttributesData())
          {
            if (cad.AttributeType.FullName == typeof(LibraryImportAttribute).FullName)
            {
              // Find EntryPoint named argument
              var entry = cad.NamedArguments.FirstOrDefault(na => na.MemberName == "EntryPoint").TypedValue.Value as string;
              if (!string.IsNullOrEmpty(entry)) entries.Add(entry!);
            }
          }
        }

        int missing = 0;
        foreach (var ep in entries)
        {
          bool ok = NativeLibrary.TryGetExport(handle, ep, out var _);
          Console.WriteLine($"  {(ok ? "[OK]" : "[!!]")} {ep}");
          if (!ok) missing++;
        }

        Console.WriteLine(missing == 0 ? "All entry points resolved." : $"Missing {missing} entry point(s). See [!!] above.");
      }
      finally
      {
        NativeLibrary.Free(handle);
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Entry point validation failed: {ex}");
    }
  }
}
