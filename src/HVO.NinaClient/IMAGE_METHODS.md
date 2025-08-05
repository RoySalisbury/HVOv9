# NINA API Client - Image Methods

This document describes the image category methods added to the NINA API client, implementing the official NINA API v2.2.6 specification.

## Image Methods

### GetImageAsync

Gets an image by index from the image history with extensive customization options.

**Method Signature:**
```csharp
Task<Result<ImageResponse>> GetImageAsync(
    int index,
    bool? resize = null,
    int? quality = null,
    string? size = null,
    bool? stream = null,
    bool? debayer = null,
    BayerPattern? bayerPattern = null,
    bool? autoPrepare = null,
    ImageType? imageType = null,
    bool? rawFits = null,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `index` (required): The index of the image to get
- `resize`: Whether to resize the image
- `quality`: The quality of the image, ranging from 1 (worst) to 100 (best). -1 or omitted for PNG
- `size`: The size of the image ([width]x[height]). Requires resize to be true
- `stream`: Stream the image to the client in JPG or PNG format
- `debayer`: Indicates if the image should be debayered
- `bayerPattern`: What bayer pattern to use for debayering (if debayer is true)
- `autoPrepare`: Setting this to true will leave all processing up to NINA
- `imageType`: Filter the image history by image type
- `rawFits`: Whether to send the image as a raw FITS format

**Supported Bayer Patterns:**
- `Monochrome`, `Color`, `RGGB`, `CMYG`, `CMYG2`, `LRGB`, `BGGR`, `GBRG`, `GRBG`, `GRGB`, `GBGR`, `RGBG`, `BGRG`

**Supported Image Types:**
- `LIGHT`, `FLAT`, `DARK`, `BIAS`, `SNAPSHOT`

**Example Usage:**
```csharp
// Get the latest image with basic settings
var result = await ninaClient.GetImageAsync(0);
if (result.IsSuccessful)
{
    var imageData = result.Value.Image; // Base64 encoded image
    var platesolveResult = result.Value.PlateSolveResult; // If solve was performed
}

// Get a resized image with specific quality
var resizedResult = await ninaClient.GetImageAsync(
    index: 0,
    resize: true,
    quality: 85,
    size: "800x600");

// Get a debayered image with specific bayer pattern
var debayeredResult = await ninaClient.GetImageAsync(
    index: 0,
    debayer: true,
    bayerPattern: BayerPattern.RGGB);
```

### GetImageHistoryAsync

Gets image history with optional filtering. Only one parameter is required.

**Method Signature:**
```csharp
Task<Result<object>> GetImageHistoryAsync(
    bool? all = null,
    int? index = null,
    bool? count = null,
    ImageType? imageType = null,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `all`: Whether to get all images or only the current image
- `index`: The index of the image to get
- `count`: Whether to count the number of images
- `imageType`: Filter by image type

**Example Usage:**
```csharp
// Get all images in history
var allImages = await ninaClient.GetImageHistoryAsync(all: true);

// Get count of all images
var imageCount = await ninaClient.GetImageHistoryAsync(count: true);

// Get count of only LIGHT frames
var lightCount = await ninaClient.GetImageHistoryAsync(
    count: true, 
    imageType: ImageType.LIGHT);

// Get specific image metadata
var imageInfo = await ninaClient.GetImageHistoryAsync(index: 5);
```

### GetImageThumbnailAsync

Gets the thumbnail of an image. This requires "Create Thumbnails" to be enabled in NINA.
The thumbnail has a width of 256px.

**Method Signature:**
```csharp
Task<Result<byte[]>> GetImageThumbnailAsync(
    int index,
    ImageType? imageType = null,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `index` (required): The index of the image to get
- `imageType`: Filter the image history by image type

**Example Usage:**
```csharp
// Get thumbnail for the latest image
var thumbnail = await ninaClient.GetImageThumbnailAsync(0);
if (thumbnail.IsSuccessful)
{
    var thumbnailBytes = thumbnail.Value; // Raw image bytes (JPG format)
    // Save to file or display in UI
    await File.WriteAllBytesAsync("thumbnail.jpg", thumbnailBytes);
}

// Get thumbnail for a specific LIGHT frame
var lightThumbnail = await ninaClient.GetImageThumbnailAsync(
    index: 3,
    imageType: ImageType.LIGHT);
```

## Models

### ImageResponse

Represents an image response that can contain base64 encoded image data and platesolve results.

```csharp
public record ImageResponse
{
    public string? Image { get; init; }
    public PlatesolveResult? PlateSolveResult { get; init; }
}
```

### ImageHistoryItem

Represents an item in the image history with comprehensive metadata.

```csharp
public record ImageHistoryItem
{
    public double ExposureTime { get; init; }
    public string ImageType { get; init; }
    public string Filter { get; init; }
    public string RmsText { get; init; }
    public string Temperature { get; init; }
    public string CameraName { get; init; }
    public int Gain { get; init; }
    public int Offset { get; init; }
    public string Date { get; init; }
    public string TelescopeName { get; init; }
    public double FocalLength { get; init; }
    public double StDev { get; init; }
    public double Mean { get; init; }
    public double Median { get; init; }
    public int Stars { get; init; }
    public double HFR { get; init; }
    public bool IsBayered { get; init; }
}
```

## Error Handling

All image methods return `Result<T>` for consistent error handling:

```csharp
var result = await ninaClient.GetImageAsync(0);
if (result.IsSuccessful)
{
    // Use result.Value
}
else
{
    // Handle result.Error
    _logger.LogError(result.Error, "Failed to get image");
}
```

## Common Error Scenarios

- **Invalid image index**: Index out of range (400 Bad Request)
- **Invalid bayer pattern**: Unsupported bayer pattern (400 Bad Request)
- **Image is not a FITS file**: When using rawFits=true on non-FITS images (400 Bad Request)  
- **No thumbnails available**: When thumbnails are not enabled in NINA (400 Bad Request)
- **API request timeout**: Network or NINA processing timeout
- **NINA not connected**: Service unavailable (500 Internal Server Error)

## API Compliance

These methods implement the NINA Advanced API v2.2.6 specification:
- `/image/{index}` endpoint with all supported parameters
- `/image-history` endpoint with filtering options
- `/image/thumbnail/{index}` endpoint for thumbnail retrieval

The implementation follows HVOv9 coding standards:
- Uses `Result<T>` pattern for error handling
- Implements structured logging with `ILogger<T>`
- Follows async/await patterns
- Uses proper parameter validation and URL encoding
