# HVOv9 (Hualapai Valley Observatory v9) Copilot Instructions

<!-- Workspace-level custom instructions for GitHub Copilot -->

## Project Overview
HVOv9 is the ninth version of the Hualapai Valley Observatory software suite, a comprehensive IoT and web application platform for observatory automation and control systems.

## Core Technologies
- .NET 9.0 (Latest LTS)
- ASP.NET Core for web applications
- Blazor Server for interactive web UI
- IoT device integration with GPIO controls
- xUnit for unit testing
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
- Use `Result<T>` pattern for operations that can fail
- Log errors with appropriate log levels (Error, Warning, Information)

### 6. Testing Standards
- Use xUnit as the primary testing framework
- Follow AAA pattern (Arrange, Act, Assert)
- Use `WebApplicationFactory<T>` for integration tests
- Mock external dependencies using Moq or similar
- Test file naming: `{ClassUnderTest}Tests.cs`

### 7. Web Development
- Use Blazor Server for interactive components with `@rendermode InteractiveServer`
- Implement API versioning with URL segments (`/api/v1.0/endpoint`)
- Use `IHttpClientFactory` for HTTP client management
- Follow REST conventions for API endpoints
- Use Bootstrap 5 for responsive UI design

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

## File Organization
```
/src
  /HVO                           # Core library
    /Iot/Devices/               # IoT device abstractions and implementations
  /HVO.ProjectName/             # Specific applications
    /Controllers/               # API/MVC controllers
    /Components/                # Blazor components
      /Pages/                   # Routable pages
      /Layout/                  # Layout components
    /Services/                  # Business logic services
    /Models/                    # Data models
    /.github/                   # Project-specific copilot instructions
  /HVO.ProjectName.Tests/       # Unit and integration tests
```

## Documentation Standards
- Use XML documentation comments for public APIs
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

## Deployment
- Support containerization with Docker when appropriate
- Use environment variables for deployment-specific configuration
- Implement health checks for web applications
- Support multiple deployment environments (Development, Staging, Production)
