# NINA API Result&lt;T&gt; Pattern Improvements

## Overview

This update improves the NINA API client's integration with the HVO Result&lt;T&gt; pattern by creating specific exception types and eliminating redundant properties from API responses.

## Key Changes

### 1. New Exception Hierarchy

Created `NinaApiException` hierarchy in `Exceptions/NinaApiException.cs`:

- **`NinaApiException`**: Base exception with HTTP status code, API error, and endpoint context
- **`NinaApiLogicalException`**: For NINA API logical errors (Success = false)
- **`NinaApiHttpException`**: For HTTP status code errors  
- **`NinaEquipmentNotFoundException`**: For missing/disconnected equipment
- **`NinaEquipmentNotAvailableException`**: For unavailable operations
- **`NinaConnectionException`**: For connection/communication failures

### 2. Leveraged Existing Response Model

The existing `NinaApiResponse<T>` in `Models/NinaApiResponse.cs` already provides the necessary structure:

- Contains essential properties: `Response`, `Error`, `Success`, `Type`, `StatusCode`
- The `StatusCode` property in the response model provides additional context alongside HTTP status codes
- No changes needed to the existing response model - it already works well with the Result<T> pattern

### 3. Exception Mapping

Created `NinaApiExceptionMapper` in `Infrastructure/NinaApiExceptionMapper.cs`:

- Maps HTTP status codes to appropriate exception types
- Analyzes error messages to determine specific equipment/operation issues
- Provides context-aware exception creation

### 4. Enhanced GetAsync&lt;T&gt; Method

Updated `GetAsync<T>` method in `NinaApiClient.cs`:

- Uses new exception hierarchy instead of generic `InvalidOperationException`
- Provides better error categorization and context
- Maintains structured logging with enhanced error information
- Includes specific exception handling for different error scenarios

## Benefits

### Better Error Handling
- **Specific Exception Types**: Callers can catch and handle specific types of NINA API errors
- **Rich Context**: Exceptions include HTTP status codes, API errors, and endpoint information
- **Equipment-Specific Errors**: Distinguish between "not found" and "not available" scenarios

### Cleaner Result&lt;T&gt; Integration
- **Leveraged Existing Structure**: Used the existing `NinaApiResponse<T>` model effectively
- **Consistent Error Patterns**: All errors are properly wrapped in Result&lt;T&gt;.Failure()
- **Improved Logging**: Structured logging with appropriate log levels and context

### Enhanced Debugging
- **Endpoint Context**: All exceptions include the endpoint that was called
- **Error Classification**: Automatically categorizes errors by type and equipment
- **Better Stack Traces**: Preserves original exceptions as inner exceptions

## Usage Examples

### Before (Generic Exceptions)
```csharp
var result = await ninaClient.ConnectCameraAsync();
if (!result.IsSuccess)
{
    // Generic InvalidOperationException - hard to determine cause
    _logger.LogError(result.Error, "Camera connection failed");
}
```

### After (Specific Exceptions)
```csharp
var result = await ninaClient.ConnectCameraAsync();
if (!result.IsSuccess)
{
    switch (result.Error)
    {
        case NinaEquipmentNotFoundException ex:
            _logger.LogWarning("Camera not found: {EquipmentType}", ex.EquipmentType);
            break;
        case NinaConnectionException ex:
            _logger.LogError(ex, "NINA connection failed");
            break;
        case NinaApiHttpException ex when ex.StatusCode == HttpStatusCode.ServiceUnavailable:
            _logger.LogWarning("NINA service unavailable, will retry later");
            break;
        default:
            _logger.LogError(result.Error, "Camera connection failed");
            break;
    }
}
```

## Migration Notes

### Existing Code Compatibility
- All public methods still return `Result<T>`
- No breaking changes to public API surface
- Existing error handling will continue to work

### Recommended Updates
- Update error handling to use specific exception types
- Take advantage of additional context properties
- Consider equipment-specific retry logic based on exception types

## Testing Considerations

- Test exception mapping for various HTTP status codes
- Verify error message parsing for equipment type detection
- Ensure proper context preservation in exception chain
- Test Result&lt;T&gt; integration with new exception types
