## Issue Description

The current Weather API endpoints lack proper OpenAPI/Swagger documentation and use generic routing that results in unclear endpoint names in the API documentation.

## Current State
- **Endpoint**: `GET /api/v1.0/weather/latest`
- **Issues**: 
  - No OpenAPI attributes for comprehensive documentation
  - Generic route results in unclear API documentation
  - Missing proper HTTP response documentation
  - Inconsistent response model documentation

## Expected Improvements

### 1. Add OpenAPI Documentation Attributes
- Add comprehensive XML documentation comments
- Add `[ProducesResponseType]` attributes for different response codes (200, 500)
- Add `[Produces]` attribute to specify response content type
- Add `[Tags]` attribute for API grouping in Swagger UI

### 2. Improve Route Documentation
- Add detailed method descriptions
- Add specific route documentation
- Ensure consistent API endpoint naming

### 3. Response Model Enhancement
- Ensure proper response DTOs with comprehensive documentation
- Add XML documentation comments for IntelliSense support
- Maintain consistent response model patterns

## Files Affected
- `src/HVO.WebSite.Playground/Controllers/WeatherController.cs`
- Weather response model classes

## Technical Impact
- Improved API documentation in Swagger UI
- Better developer experience for API consumers
- More maintainable and testable code structure
