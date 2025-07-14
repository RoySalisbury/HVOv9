## Issue Description

The current `PingController` API endpoint lacks proper OpenAPI/Swagger documentation and uses generic routing that results in unclear endpoint names in the API documentation.

## Current State
- **Endpoint**: `GET /api/v1.0/ping`
- **Method name**: `Get()`
- **Issues**: 
  - No OpenAPI attributes for documentation
  - Generic route results in unclear API documentation
  - Anonymous response object instead of proper DTO
  - Missing proper HTTP response documentation

## Expected Improvements

### 1. Add OpenAPI Documentation Attributes
- Add `[OpenApiOperation]` attribute with proper summary and description
- Add `[OpenApiResponse]` attributes for different response codes (200, 500)
- Add `[Produces]` attribute to specify response content type
- Add `[Tags]` attribute for API grouping in Swagger UI

### 2. Improve Route Naming
- Rename method from `Get()` to `HealthCheck()` or `Ping()`
- Add specific route like `[HttpGet("health")]` or `[HttpGet("status")]`
- **Result**: `/api/v1.0/ping/health` or `/api/v1.0/ping/status`

### 3. Response Model Enhancement
- Create a proper response DTO/model instead of anonymous object
- Add XML documentation comments for IntelliSense support
- Consider implementing `PingResponse` class with proper properties

## Files Affected
- `src/HVO.WebSite.Playground/Controllers/PingController.cs`
- May need to add response model class

## Technical Impact
- Improved API documentation in Swagger UI
- Better developer experience for API consumers
- More maintainable and testable code structure
