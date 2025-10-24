#nullable enable

namespace HVO.Astronomy.CFITSIO;

/// <summary>
/// Policy for (re)compressing FITS images. For simple whole-file compression
/// use <see cref="FitsFile.CompressTo(string)"/> (CFITSIO's defaults).
/// Use this policy with <see cref="FitsFile.ApplyCompressionPolicyToCurrentHdu(FitsCompressionPolicy)"/>
/// when you need to control tiling/parameters per HDU.
/// </summary>
public sealed class FitsCompressionPolicy
{
  /// <summary>Compression algorithm. Use <see cref="FitsCompression.None"/> for none.</summary>
  public FitsCompression Compression { get; init; } = FitsCompression.Rice;

  /// <summary>
  /// Optional tile dimensions. For 2D images: <c>{ tileWidth, tileHeight }</c>.
  /// Length should equal the number of axes.
  /// </summary>
  public long[]? TileDimensions { get; init; }

  /// <summary>
  /// Optional algorithm-specific parameters (e.g., HCOMPRESS scale factor).
  /// See CFITSIO documentation for valid parameters per algorithm.
  /// </summary>
  public float[]? Parameters { get; init; }

  /// <summary>If true, write HDU checksums after applying settings.</summary>
  public bool WriteChecksum { get; init; } = true;
}
