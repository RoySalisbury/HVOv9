#nullable enable
using HVO.Astronomy.CFITSIO.Interop;

namespace HVO.Astronomy.CFITSIO;

/// <summary>
/// Compression algorithms supported by CFITSIO for tiled image compression.
/// </summary>
public enum FitsCompression
{
  /// <summary>No compression.</summary>
  None = 0,

  /// <summary>Rice compression (RICE_1).</summary>
  Rice = CFitsIO.RICE_1,

  /// <summary>GZIP_1 compression (row-by-row, default variant).</summary>
  GZip1 = CFitsIO.GZIP_1,

  /// <summary>GZIP_2 compression (tiled variant).</summary>
  GZip2 = CFitsIO.GZIP_2,

  /// <summary>HCOMPRESS (wavelet-based) compression.</summary>
  HCompress = CFitsIO.HCOMPRESS_1,

  /// <summary>PLIO compression (integer-only, limited dynamic range).</summary>
  Plio = CFitsIO.PLIO_1
}
