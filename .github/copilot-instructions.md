# HVOv9 (Hualapai Valley Observatory v9) Copilot Instructions

<!-- Workspace-level custom instructions for GitHub Copilot -->

## Project Overview
HVOv9 is the ninth version of the Hualapai Valley Observatory software suite, a comprehensive IoT and web application platform for observatory automation and control systems.

## Core Technologies
- .NET 9.0 (Latest LTS)
- ASP.NET Core for web applications
- Blazor Server for interactive web UI
- Entity Framework Core for data access
- IoT device integration with GPIO controls
- xUnit with FluentAssertions for unit testing
- Moq for service mocking and test isolation
- Azure DevOps for CI/CD

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

### 5. Error Handling
- Use structured logging with `ILogger<T>`
- Implement proper exception handling with specific exception types
- **Use `Result<T>` pattern for operations that can fail** - Located in `HVO/Result.cs`
- **Use `InvalidOperationException` for true "not found" scenarios only** - Controllers should handle these explicitly
- **Service state issues should use `InvalidOperationException` but return 500 via controller handling**
- Implement global exception handling middleware (`HvoServiceExceptionHandler`)
- Log errors with appropriate log levels (Error, Warning, Information)

### 6. Testing Standards
- Use xUnit as the primary testing framework
- Follow AAA pattern (Arrange, Act, Assert)
- Use `WebApplicationFactory<T>` for integration tests
- Mock external dependencies using Moq or similar
- **Use service mocking instead of database seeding for integration tests**
- **Create enhanced TestWebApplicationFactory with proper service replacement**
- Use FluentAssertions for readable test assertions
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

### 8. IoT Device Integration
- Implement proper disposal patterns (`IDisposable`, `IAsyncDisposable`)
- Use event-driven patterns for device state changes
- Handle GPIO operations with proper error handling
- Use abstractions for testable device interactions

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

## HVOv9-Specific Patterns

### 1. Result<T> Pattern Usage
- All service methods that can fail should return `Result<T>`
- Use `Result<T>.Success(value)` for successful operations
- Use `Result<T>.Failure(exception)` for failed operations
- Controllers should handle Result<T> and convert to appropriate HTTP responses
- Use `InvalidOperationException` for 404/NotFound scenarios

### 2. API Response Models
- Create dedicated response models for all API endpoints
- Use consistent naming: `LatestWeatherResponse`, `CurrentWeatherResponse`, etc.
- Implement proper JSON serialization attributes when needed

### 3. Integration Test Patterns
- Use `TestWebApplicationFactory` with service mocking instead of database seeding
- Mock all external dependencies including database services
- Create test data builders for consistent test data generation
- Group tests by functionality using `#region` blocks
- Test both success and failure scenarios for all endpoints
- **Test controller error handling patterns with Result<T> failures**

### 4. Service Layer Architecture
- Create interfaces for all services (`IWeatherService`, etc.)
- Implement business logic in service classes, not controllers
- Use dependency injection for all service dependencies
- Return `Result<T>` from all service methods that can fail
- **Use `InvalidOperationException` for true "not found" scenarios only**
- **Service state issues should use `InvalidOperationException` but be handled as 500 errors**
- Log important operations and errors appropriately

### 5. Exception Handling Middleware
- Use `HvoServiceExceptionHandler` for global exception handling
- **Controllers should handle Result<T> failures explicitly** using pattern matching
- **Only use InvalidOperationException for 404 responses when it's truly a "not found" scenario**
- **Service state issues (e.g., "not initialized") should return 500 Internal Server Error**
- Provide consistent `ProblemDetails` responses for errors
- Log exceptions with appropriate context and severity

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

## Azure DevOps Integration
- Use Azure DevOps for CI/CD pipelines and work item tracking
- Azure DevOps CLI is available for work item management

### Work Item Creation Best Practices
- Use `--fields` parameter to specify Description and Acceptance Criteria separately
- Field format: `"Description=<content>" "Acceptance Criteria=<content>"`
- Priority field: `"Microsoft.VSTS.Common.Priority=3"` (1=Critical, 2=High, 3=Medium, 4=Low)
- Leave `--assigned-to` blank on initial creation unless specifically requested

### Work Item Lifecycle Management
- **Bug States**: New → Active → Resolved → Closed
- Always assign work items when beginning development: `--assigned-to "user@domain.com"`
- Update state progression:
  - **New**: Initial creation state
  - **Active**: When development begins (`--state "Active" --fields "System.Reason=Development Started"`)
  - **Resolved**: When fix is complete and PR created (`--state "Resolved" --fields "System.Reason=Fixed"`)
  - **Closed**: When changes are deployed and verified in production
- Update work items throughout the development process to maintain accurate project status

### Content Formatting for Azure DevOps
- **Use HTML formatting instead of Markdown** for work item descriptions and acceptance criteria
- HTML formatting renders properly in Azure DevOps web interface
- Use HTML tags: `<h2>`, `<h3>`, `<ul>`, `<li>`, `<code>`, `<strong>`, `<p>`
- Example structure:
  ```html
  <h2>Issue Description</h2>
  <p>Description content with <code>code examples</code></p>
  <h3>Subsection</h3>
  <ul>
    <li>List item with <strong>emphasis</strong></li>
    <li>Code reference: <code>[HttpGet("endpoint")]</code></li>
  </ul>
  ```
- For acceptance criteria, use simple `<li>` elements instead of checkboxes for better readability

## Deployment
- Support containerization with Docker when appropriate
- Use environment variables for deployment-specific configuration
- Implement health checks for web applications
- Support multiple deployment environments (Development, Staging, Production)
