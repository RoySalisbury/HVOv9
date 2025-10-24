using System;
using System.IO;

namespace HVO.Astronomy.CFITSIO.Tests.Helpers;

internal static class TestPaths
{
  public static string TempDir => Path.Combine(Path.GetTempPath(), "HVOv9.CFITSIO.Tests");

  public static string GetTempFile(string name)
  {
    Directory.CreateDirectory(TempDir);
    return Path.Combine(TempDir, name);
  }

  public static void DeleteIfExists(string path)
  {
    try
    {
      if (File.Exists(path)) File.Delete(path);
    }
    catch
    {
      // ignore
    }
  }
}
