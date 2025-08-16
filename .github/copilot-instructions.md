# HVOv9 (Hualapai Valley Observatory v9) Copilot Instructions

<!-- Workspace-level custom instructions for GitHub Copilot -->

## Project Overview
HVOv9 is the ninth version of the Hualapai Valley Observatory software suite, a comprehensive IoT and web application platform for observatory automation and control systems.

## Core Technologies
- .NET 9.0 (STS; SDK pinned via global.json)
- ASP.NET Core for web applications
- Blazor Server for interactive web UI
- Entity Framework Core for data access
- IoT device integration with GPIO controls
- MSTest for unit and integration testing
- Moq (optional) for service mocking and test isolation
- GitHub + VS Code Dev Containers for development

## HVOv9 Coding Standards

### 1. Project Structure
- Use explicit namespaces matching folder structure
- Organize code into logical layers: Controllers, Services, Models, etc.
- Place shared code in the main HVO project
- Use separate test projects with `.Tests` suffix

### 2. C# Language Features
- **NO top-level statements** - Always use explicit `Main` method with proper class structure
- Use C# 12 features appropriately (primary constructors, collection expressions, etc.)
- Use `var` for local variables when type is obvious
- Prefer `async/await` patterns over `.Result` or `.Wait()`
- Use nullable reference types and proper null handling

### 3. Method and Class Structure
```csharp
namespace HVO.ProjectName
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Application entry point
        }
    }
}
```

### 4. Dependency Injection
- Use constructor injection for required dependencies
- Register services in `Program.cs` using `builder.Services`
- Use `IServiceCollection` extension methods for complex service registration
- Prefer interfaces for testability

### 5. Error Handling and Logging Standards
- **Use structured logging with `ILogger<T>` throughout the workspace** - Follow consistent patterns across all components
- **Dependency Injection for Logging**: Use constructor injection with optional `ILogger<T>?` parameters in all hardware device classes
- **Log Level Guidelines**: 
  - `Trace`: High-frequency operations like timer events and GPIO state toggles
  - `Debug`: Operational state changes, method entry/exit, configuration changes
  - `Information`: Important business events, startup/shutdown, major state transitions
  - `Warning`: Recoverable errors, configuration issues, performance concerns
  - `Error`: Exceptions and unrecoverable errors with full context
  - `Critical`: System-level failures requiring immediate attention
- **Structured Data**: Use named parameters in log messages for better searchability and monitoring
- **Hardware Device Logging**: All GPIO and IoT device classes must support ILogger<T> with fallback creation when not provided
- Implement proper exception handling with specific exception types
- **Use `Result<T>` pattern for operations that can fail** - Located in `HVO/Result.cs`
- **Use `InvalidOperationException` for true "not found" scenarios only** - Controllers should handle these explicitly
- **Service state issues should use `InvalidOperationException` but return 500 via controller handling**
- Implement global exception handling middleware (`HvoServiceExceptionHandler`)
- **Replace Debug.WriteLine with structured logging** - No console debugging in production code

### 6. Testing Standards
- Use MSTest as the primary testing framework
- Follow AAA pattern (Arrange, Act, Assert)
- Use `WebApplicationFactory<T>` for integration tests
- Mock external dependencies using Moq or similar
- **Use service mocking instead of database seeding for integration tests**
- **Create enhanced TestWebApplicationFactory with proper service replacement**
- FluentAssertions is optional
- Test file naming: `{ClassUnderTest}Tests.cs`
- **Suppress CS1030 warnings in test projects for clean builds**

### 7. Blazor Component Development (Following Microsoft Best Practices)
- Use Blazor Server for interactive components with `@rendermode InteractiveServer`
- **Always create code-behind files (.razor.cs) for all Razor components and pages** - Separate markup from logic for better maintainability and testability
- **Use Blazor Scoped CSS (.razor.css) for component-specific styling** - Automatically scoped to prevent style conflicts
- **Use Blazor Scoped JavaScript (.razor.js) for component-specific client-side behavior** - Isolated JavaScript modules
- **NO inline CSS in Razor markup** - All styling must be in scoped CSS files
- **NO inline JavaScript in Razor markup** - All client-side code must be in scoped JS files or code-behind
- **Component File Structure Pattern**:
  ```
  ComponentName.razor      # Markup only - no <style> or <script> blocks
  ComponentName.razor.cs   # C# logic and event handlers
  ComponentName.razor.css  # Scoped styles (automatically scoped by Blazor)
  ComponentName.razor.js   # Scoped JavaScript (optional, for client interop)
  ```
- Implement API versioning with URL segments (`/api/v1.0/endpoint`)
- Use `IHttpClientFactory` for HTTP client management
- Follow REST conventions for API endpoints
- Use Bootstrap 5 for responsive UI design with component-specific customizations in scoped CSS

### 8. IoT Device Integration and Hardware Standards
- **Consistent Logging**: All hardware device classes must implement ILogger<T> support with optional dependency injection
- **Hardware Device Constructor Pattern**: Include optional `ILogger<DeviceClass>? logger = null` parameter in all constructors
- **Internal Logger Creation**: When logger is not provided via DI, create internal logger or use NullLogger pattern
- **GPIO State Logging**: Use appropriate log levels for hardware operations:
  - `Trace` for pin state changes and high-frequency events
  - `Debug` for device initialization, configuration changes, and operational state
  - `Information` for important device lifecycle events
  - `Error` for hardware failures with full context including pin numbers and error details
- **Thread-Safe Logging**: Ensure all logging calls are thread-safe, especially in timer callbacks and GPIO event handlers
- **Structured Context**: Include relevant hardware context (pin numbers, device states, timing) in all log messages
- Implement proper disposal patterns (`IDisposable`, `IAsyncDisposable`)
- Use event-driven patterns for device state changes
- Handle GPIO operations with proper error handling
- Use abstractions for testable device interactions
- **Follow GpioLimitSwitch.cs pattern for exemplary logging implementation**

### 9. Configuration Management
- Use `appsettings.json` for configuration
- Support environment-specific settings (`appsettings.Development.json`)
- Use strongly-typed configuration with `IOptions<T>`
- Validate configuration at startup

### 10. Performance Considerations
- Use `ValueTask<T>` for potentially synchronous async operations
- Implement proper caching strategies where appropriate
- Use `Span<T>` and `Memory<T>` for high-performance scenarios
- Dispose of resources properly

### 11. Development Workflow Standards
- **VS Code Dev Container**: Use the provided Dev Container; ports 5136 (HTTP) and 7151 (HTTPS) are forwarded by default
- **VS Code Launch**: Use the provided launch configs (.NET Debug/.NET Release). They build first and auto-open the browser
- **HTTP/HTTPS**: Development disables HTTPS redirection by default; an HTTP-only profile is available to avoid cert prompts
- **Configuration Loading**: Launch configurations run from the output directory (`bin/<Config>/net9.0`) to ensure config files are loaded consistently
- **Process Management**: Use the `kill:playground` task to free ports 5136/7151 before relaunch
- **Build Before Run**: Build prior to run is handled by VS Code tasks
- **Threading in Blazor**:
  - All `StateHasChanged()` calls from background threads must use `InvokeAsync()`
  - Pattern: `await InvokeAsync(StateHasChanged);`
  - Timer and event handlers from non-UI threads require thread-safe UI updates
- **Timer Management for Safety Systems**:
  - Always dispose and recreate `System.Timers.Timer` for reliable restart behavior
  - Set `AutoReset = false` for one-time safety triggers
  - Use timer recreation pattern instead of `Start()/Stop()` for safety-critical scenarios
- **UI Component Logging**: 
  - Clean up excessive debug logging from timer and property operations
  - Use Trace level for high-frequency UI events, Debug for user interactions
  - Maintain essential operational logging for troubleshooting user interface issues

## HVOv9-Specific Patterns

### 1. Logging Standardization Patterns
- **Workspace-Wide Consistency**: All classes across the workspace must follow the same ILogger<T> patterns
- **Hardware Device Logger Pattern**: 
  ```csharp
  private readonly ILogger<DeviceClass>? _logger;
  
  public DeviceClass(..., ILogger<DeviceClass>? logger = null)
  {
      _logger = logger;
      // Optional: Create fallback logger if needed
  }
  ```
- **Structured Logging Template**: Use consistent named parameter patterns across all log messages
  ```csharp
  _logger?.LogDebug("Operation Started - Parameter: {ParameterName}, State: {CurrentState}", parameterValue, currentState);
  ```
- **Log Level Consistency**: 
  - Hardware classes: Trace for pin operations, Debug for state changes, Information for lifecycle
  - Service classes: Debug for business operations, Information for important events, Error for failures
  - UI components: Trace for timers/properties, Debug for user interactions, clean up excessive logging
- **Error Context Standardization**: Always include relevant context (pin numbers, device states, timing, IDs) in error logs
- **Performance-Sensitive Logging**: Use Trace level for high-frequency operations to avoid performance impact

### 2. Result<T> Pattern Usage
- All service methods that can fail should return `Result<T>`
- Use `Result<T>.Success(value)` for successful operations
- Use `Result<T>.Failure(exception)` for failed operations
- Controllers should handle Result<T> and convert to appropriate HTTP responses
- Use `InvalidOperationException` for 404/NotFound scenarios

### 3. API Response Models
- Create dedicated response models for all API endpoints
- Use consistent naming: `LatestWeatherResponse`, `CurrentWeatherResponse`, etc.
- Implement proper JSON serialization attributes when needed

### 4. Integration Test Patterns
- Use `TestWebApplicationFactory` with service mocking instead of database seeding
- Mock all external dependencies including database services
- Create test data builders for consistent test data generation
- Group tests by functionality using `#region` blocks
- Test both success and failure scenarios for all endpoints
- **Test controller error handling patterns with Result<T> failures**

### 5. Service Layer Architecture and Logging Standards
- Create interfaces for all services (`IWeatherService`, etc.)
- Implement business logic in service classes, not controllers
- Use dependency injection for all service dependencies including ILogger<T>
- Return `Result<T>` from all service methods that can fail
- **Standardized Service Logging**: All service classes must use structured ILogger<T> patterns
- **Service Method Logging**: Log entry/exit for complex operations, state changes, and error conditions
- **Consistent Error Context**: Include relevant business context in error logs (IDs, states, timing)
- **Performance Logging**: Use Debug level for performance-sensitive operations, Trace for high-frequency calls
- **Use `InvalidOperationException` for true "not found" scenarios only**
- **Service state issues should use `InvalidOperationException` but be handled as 500 errors**
- Log important operations and errors appropriately using structured logging patterns

### 6. Exception Handling Middleware
- Use `HvoServiceExceptionHandler` for global exception handling
- **Controllers should handle Result<T> failures explicitly** using pattern matching
- **Only use InvalidOperationException for 404 responses when it's truly a "not found" scenario**
- **Service state issues (e.g., "not initialized") should return 500 Internal Server Error**
- Provide consistent `ProblemDetails` responses for errors
- Log exceptions with appropriate context and severity

### 7. HTTPS and Local Development
- Development environment:
  - `EnableHttpsRedirect` is false by default (no forced redirect to HTTPS)
  - `TrustDevCertificates` is true by default so LocalApi HttpClient can call local endpoints over HTTPS when needed
- Launch profiles bind to `https://0.0.0.0:7151;http://0.0.0.0:5136` and auto-open the site
- Dev certificates are provided by the dev container; no local export script or `.certs/https-devcert.pfx` is required.

### 8. NINA API Integration (HVO.NinaClient)
- **Official API Specifications**: NINA (N.I.N.A. - Nighttime Imaging 'N' Astronomy) API specifications are maintained at:
  - **REST API Specification**: https://github.com/christian-photo/ninaAPI/blob/main/ninaAPI/api_spec.yaml
  - **WebSocket/AsyncAPI Specification**: https://github.com/christian-photo/ninaAPI/blob/main/ninaAPI/websocket_spec.yaml
- **API Response Types**: Always consult official specifications for correct response types
  - Equipment connection methods return descriptive strings (e.g., "Connected", "Disconnected")
  - Status queries return structured data objects
- **Result<T> Pattern**: All NINA API client methods should return `Result<string>` or `Result<T>` based on specification
- **API Method Categories**:
  - Connection management: Connect/Disconnect operations typically return status strings
  - Equipment control: Commands and actions with varied response types per specification
  - Status queries: Return equipment state and configuration data
- **Error Handling**: NINA API errors should be wrapped in `Result<T>.Failure()` with appropriate exception context

## File Organization
```
/src
  /HVO                           # Core library
    /Result.cs                  # Result<T> pattern implementation
    /ComponentModel/            # Component model extensions
    /Iot/Devices/               # IoT device abstractions and implementations
  /HVO.DataModels/              # Entity Framework models and context
    /Data/                      # Database context and configurations
    /Models/                    # Entity models
    /RawModels/                 # Raw device data models
    /Repositories/              # Repository pattern implementations
  /HVO.ProjectName/             # Specific applications
    /Controllers/               # API/MVC controllers
    /Components/                # Blazor components with proper file structure
      /Pages/                   # Routable pages with .razor, .razor.cs, .razor.css, .razor.js
        ComponentName.razor     # Markup only - no <style> or <script> blocks
        ComponentName.razor.cs  # C# code-behind logic
        ComponentName.razor.css # Scoped CSS (automatically scoped by Blazor)
        ComponentName.razor.js  # Scoped JavaScript (optional, for client interop)
      /Layout/                  # Layout components following same structure
    /Services/                  # Business logic services
    /Models/                    # API response models and DTOs
    /Middleware/                # Custom middleware (exception handling)
  /HVO.ProjectName.Tests/       # Unit and integration tests
    /Controllers/               # Controller unit tests
    /Services/                  # Service layer tests
    /Integration/               # Integration tests with TestWebApplicationFactory
    /Core/                      # Core pattern tests (Result<T>, etc.)
    /TestHelpers/               # Test utilities and factories
```

## Blazor Component Best Practices

### Component File Structure (Microsoft Recommended)
- **Separation of Concerns**: Each component should have distinct files for markup, logic, styling, and client-side behavior
- **Scoped CSS**: Use `.razor.css` files for component-specific styling that won't affect other components
- **Scoped JavaScript**: Use `.razor.js` files for component-specific client-side code with automatic isolation
- **Code-Behind**: Use `.razor.cs` files for all C# logic to keep markup clean and testable

### Styling Standards
- **Scoped CSS Only**: Never use inline `<style>` blocks in Razor markup
- **Automatic Scoping**: Blazor automatically generates unique CSS selectors for scoped CSS
- **Bootstrap Integration**: Use Bootstrap classes with component-specific customizations in scoped CSS
- **No Global CSS for Components**: Component-specific styles must be in scoped CSS files

### JavaScript Standards
- **Scoped JavaScript Only**: Never use inline `<script>` blocks in Razor markup
- **Module Isolation**: Scoped JS files are automatically treated as ES6 modules
- **Component Lifecycle**: Use scoped JS for component-specific DOM manipulation and client interop
- **Automatic Loading**: Blazor automatically loads and unloads scoped JS with component lifecycle

## Documentation Standards
- XML documentation comments are optional but recommended for complex public APIs
- Focus on clear, meaningful code that is self-documenting through good naming
- Include README.md files for complex projects
- Document configuration options and environment setup
- Provide usage examples for public APIs

## Git Workflow
- Use meaningful commit messages
- Create feature branches for new development
- Use pull requests for code review
- Tag stable releases with semantic versioning

## Security Considerations
- Use HTTPS in production environments
- Implement proper input validation
- Use parameterized queries for database operations
- Follow OWASP security guidelines
- Validate and sanitize user inputs

## GitHub & CI/CD
- Use GitHub for repo hosting and pull requests
- For CI/CD, use GitHub Actions (not configured here). If needed later, add workflows under `.github/workflows/`

## Deployment
- Support containerization with Docker when appropriate
- Use environment variables for deployment-specific configuration
- Implement health checks for web applications
- Support multiple deployment environments (Development, Staging, Production)
