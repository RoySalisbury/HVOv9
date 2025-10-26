namespace HVO.SkyMonitorV5.RPi.Pipeline;

/// <summary>
/// FITS-specific encoding options for image export.
/// </summary>
public sealed record FitsEncodingOptions
{
    /// <summary>
    /// Bit depth for FITS image data. Default is U16.
    /// </summary>
    public FitsBitDepth BitDepth { get; init; } = FitsBitDepth.U16;

    /// <summary>
    /// Image format (mono, RGB, etc.). Default is Mono for current grayscale pipeline.
    /// </summary>
    public FitsImageFormat ImageFormat { get; init; } = FitsImageFormat.Mono;

    /// <summary>
    /// Compression algorithm. Default is None.
    /// </summary>
    public FitsCompression Compression { get; init; } = FitsCompression.None;

    /// <summary>
    /// When true and BitDepth=U16, write unsigned scaling (BSCALE=1, BZERO=32768).
    /// Default is true for compatibility with common astronomical software.
    /// </summary>
    public bool UnsignedU16 { get; init; } = true;

    /// <summary>
    /// When true, write FITS checksum keywords for data integrity verification.
    /// Default is true for data integrity.
    /// </summary>
    public bool WriteChecksum { get; init; } = true;
}

/// <summary>
/// Bit depth options for FITS image data.
/// </summary>
public enum FitsBitDepth
{
    /// <summary>8-bit unsigned integer (0-255)</summary>
    U8 = 0,
    
    /// <summary>16-bit unsigned integer (0-65535)</summary>
    U16 = 1,
    
    /// <summary>16-bit signed integer (-32768 to 32767)</summary>
    I16 = 2,
    
    /// <summary>32-bit signed integer</summary>
    I32 = 3,
    
    /// <summary>32-bit floating point</summary>
    F32 = 4,
    
    /// <summary>64-bit floating point</summary>
    F64 = 5
}

/// <summary>
/// Image format options for FITS files.
/// </summary>
public enum FitsImageFormat
{
    /// <summary>Single-channel grayscale image (current pipeline)</summary>
    Mono = 0,
    
    /// <summary>Three-channel RGB image (future color support)</summary>
    RGB = 1,
    
    /// <summary>Four-channel RGBA image (future with alpha channel)</summary>
    RGBA = 2,
    
    /// <summary>Raw Bayer mosaic pattern (future direct sensor data)</summary>
    BayerMosaic = 3
}

/// <summary>
/// Compression algorithms supported for FITS files.
/// </summary>
public enum FitsCompression
{
    /// <summary>No compression (default)</summary>
    None = 0,
    
    /// <summary>Rice compression algorithm (lossless, good for integer data)</summary>
    Rice = 1,
    
    /// <summary>GZIP compression level 1 (fastest)</summary>
    Gzip1 = 2,
    
    /// <summary>GZIP compression level 2 (balanced)</summary>
    Gzip2 = 3,
    
    /// <summary>H-Compress algorithm (lossy/lossless)</summary>
    HCompress = 4,
    
    /// <summary>PLIO compression (lossless, good for masks)</summary>
    PLio = 5
}
