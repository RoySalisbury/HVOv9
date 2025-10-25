using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HVO.Astronomy.CFITSIO.Interop;

/// <summary>
/// Ensures the native CFITSIO library can be resolved when running from test hosts or
/// other contexts where the default probing paths don't include runtimes/&lt;rid&gt;/native.
/// </summary>
internal static class NativeLibraryResolver
{
  #pragma warning disable CA2255 // Allow module initializer in this library for native resolution
  private const string LibBaseName = "cfitsio";

  [ModuleInitializer]
  internal static void Initialize()
  {
    // Register a resolver for the assembly containing the P/Invoke declarations
    NativeLibrary.SetDllImportResolver(typeof(CFitsIO).Assembly, Resolve);
  }

  private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
  {
    try
    {
      if (!string.Equals(libraryName, LibBaseName, StringComparison.OrdinalIgnoreCase))
        return IntPtr.Zero; // Not our library; fall back to default resolution

      var asmDir = Path.GetDirectoryName(assembly.Location);
      if (string.IsNullOrEmpty(asmDir)) return IntPtr.Zero;

      var rid = RuntimeInformation.RuntimeIdentifier; // e.g., osx-arm64, linux-x64, win-x64

      // Candidate filenames per platform
      var candidates = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        ? new[] { "libcfitsio.dylib" }
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
          ? new[] { "libcfitsio.so" }
          : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "cfitsio.dll" }
            : Array.Empty<string>();

      // Probe typical locations:
      // 1) runtimes/<rid>/native/<name>
      // 2) same directory as the managed assembly
      // 3) macOS Homebrew fallback (best-effort)
      foreach (var file in candidates)
      {
        var ridPath = Path.Combine(asmDir, "runtimes", rid ?? string.Empty, "native", file);
        if (File.Exists(ridPath))
        {
          try { return NativeLibrary.Load(ridPath); } catch { /* continue */ }
        }

        var sameDir = Path.Combine(asmDir, file);
        if (File.Exists(sameDir))
        {
          try { return NativeLibrary.Load(sameDir); } catch { /* continue */ }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
          // Common Homebrew install name used by libcfitsio
          var brewPath = "/opt/homebrew/opt/cfitsio/lib/" + file;
          if (File.Exists(brewPath))
          {
            try { return NativeLibrary.Load(brewPath); } catch { /* continue */ }
          }
        }
      }

      // Let the runtime continue with its default resolution
      return IntPtr.Zero;
    }
    catch
    {
      // If anything goes wrong, fall back so the runtime can throw the original exception type
      return IntPtr.Zero;
    }
  }
}

#pragma warning restore CA2255
