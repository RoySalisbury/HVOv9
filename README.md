# HVOv9 - Hualapai Valley Observatory v9

The ninth version of the Hualapai Valley Observatory software suite, a comprehensive IoT and web application platform for observatory automation and control systems.

## Overview

HVOv9 is a modern .NET 9.0-based platform that provides:
- **Observatory Automation**: IoT device control and monitoring
- **Weather Data Management**: Real-time weather station integration and API
- **Web Interface**: Blazor Server-based interactive dashboard
- **Roof Control**: Automated observatory roof control system
- **Comprehensive Testing**: Full test coverage with unit and integration tests

## Technology Stack

- **.NET 9.0** - Latest LTS framework
- **ASP.NET Core** - Web applications and APIs
- **Blazor Server** - Interactive web UI with `@rendermode InteractiveServer`
- **Entity Framework Core** - Data access and modeling
- **MSTest** - Comprehensive testing framework with dependency injection support
- **IoT Device Integration** - GPIO controls and hardware interfaces
- **Azure DevOps** - CI/CD pipeline

## Project Structure

```
src/
├── HVO/                              # Core library and shared components
│   ├── Result.cs                     # Result<T> pattern implementation
│   ├── ResultUsageExamples.cs       # Usage examples for Result pattern
│   ├── ComponentModel/               # Component model extensions
│   │   └── AttributeExtensions.cs   # Attribute utility extensions
│   └── Iot/                         # IoT device abstractions
│       └── Devices/                 # Device implementations
│
├── HVO.DataModels/                   # Data models and Entity Framework context
│   ├── Data/                        # Database context and configurations
│   │   └── HvoDbContext.cs          # Main EF Core database context
│   ├── Models/                      # Entity models for weather and device data
│   ├── RawModels/                   # Raw data models for device input
│   ├── Repositories/                # Repository pattern implementations
│   └── Extensions/                  # Service collection extensions
│
├── HVO.WebSite.Playground/          # Main web application
│   ├── Controllers/                 # API controllers (Weather, Ping, Home)
│   ├── Components/                  # Blazor components
│   │   ├── Pages/                   # Routable pages with .razor.cs code-behind
│   │   └── Layout/                  # Layout components
│   ├── Services/                    # Business logic services
│   │   ├── IWeatherService.cs       # Weather service interface
│   │   └── WeatherService.cs        # Weather service implementation
│   ├── Models/                      # API response models
│   │   └── WeatherApiModels.cs      # Weather API DTOs and responses
│   ├── Middleware/                  # Custom middleware
│   │   └── HvoServiceExceptionHandler.cs # Global exception handling
│   └── wwwroot/                     # Static web assets
│
├── HVO.WebSite.Playground.Tests/    # Comprehensive test suite
│   ├── Controllers/                 # Controller unit tests
│   │   └── WeatherControllerTests.cs # Weather API controller tests
│   ├── Services/                    # Service layer tests
│   │   └── WeatherServiceTests.cs   # Weather service business logic tests
│   ├── Integration/                 # Integration tests
│   │   └── WeatherApiIntegrationTests.cs # Full HTTP API contract tests
│   ├── Core/                        # Core pattern tests
│   │   └── ResultPatternTests.cs    # Result<T> pattern validation
│   └── TestHelpers/                 # Test utilities and factories
│       ├── TestWebApplicationFactory.cs # Enhanced test server factory
│       ├── WeatherTestDataBuilder.cs # Test data generation
│       ├── TestDbContextFactory.cs  # Database test utilities
│       └── TestUtilities.cs         # Common test utilities
│
├── HVO.Iot.Devices.Tests/           # IoT device testing
│   ├── GpioButtonWithLedTests.cs    # GPIO button and LED component tests
│   ├── GpioLimitSwitchTests.cs      # Limit switch hardware tests
│   └── MSTestSettings.cs            # Test configuration
│
├── HVO.GpioTestApp/                  # GPIO testing and validation application
│   └── Program.cs                   # GPIO device testing console app
│
├── HVO.WebSite.RoofControllerV4/     # Observatory roof control system
│   ├── Controllers/                 # Roof control API endpoints
│   ├── HostedServices/              # Background services for automation
│   └── Logic/                       # Roof control business logic
│
└── HVOv9.slnx                       # Solution file
```

## Getting Started

### Prerequisites

- **.NET 9.0 SDK** or later
- **Visual Studio 2022** or **VS Code** with C# extension
- **Git** for version control

### Installation

1. **Clone the repository:**
   ```bash
   git clone https://royjs.visualstudio.com/Hualapai%20Valley%20Observatory%20-%20v9/_git/HVOv9
   cd HVOv9/src
   ```

2. **Restore dependencies:**
   ```bash
   dotnet restore
   ```

3. **Build the solution:**
   ```bash
   dotnet build
   ```

4. **Run tests:**
   ```bash
   dotnet test
   ```

### Running the Applications

#### Web Playground Application
```bash
cd HVO.WebSite.Playground
dotnet run
```
Access at: `https://localhost:5001` or `http://localhost:5000`

#### Roof Controller Application
```bash
cd HVO.WebSite.RoofControllerV4
dotnet run
```

#### GPIO Test Application
```bash
cd HVO.GpioTestApp
dotnet run
```

## API Documentation

### Weather API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/v1.0/weather/latest` | GET | Get the most recent weather record |
| `/api/v1.0/weather/current` | GET | Get current weather conditions |
| `/api/v1.0/weather/highs-lows` | GET | Get weather highs/lows for date range |

#### Example Usage
```bash
# Get latest weather data
curl https://localhost:5001/api/v1.0/weather/latest

# Get weather highs/lows for specific date range
curl "https://localhost:5001/api/v1.0/weather/highs-lows?startDate=2025-07-01&endDate=2025-07-13"
```

## Build and Test

### Building
```bash
# Build entire solution
dotnet build

# Build specific project
dotnet build HVO.WebSite.Playground

# Build for Release
dotnet build --configuration Release
```

### Testing
```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --verbosity normal

# Run specific test project
dotnet test HVO.WebSite.Playground.Tests

# Run tests for both Debug and Release configurations
dotnet test --configuration Debug
dotnet test --configuration Release
```

### Test Coverage
- **140+ total tests** across the solution
- **MSTest framework** with dependency injection support
- **Unit tests** for individual components and services
- **Integration tests** for web APIs and IoT devices
- **Mock GPIO controllers** for hardware-independent testing
- **Comprehensive API contract validation** with integration tests

### Testing Framework
The solution uses **MSTest** exclusively for consistency and maintainability:
- `[TestClass]` for test class definition
- `[TestMethod]` for individual test methods
- `[DataRow]` for parameterized tests
- `[TestInitialize]` and `[TestCleanup]` for setup/teardown
- Dependency injection integration for IoT testing scenarios

See `MSTest_Standardization.md` for complete testing guidelines and migration details.

## Coding Standards

The project follows the HVOv9 coding standards as outlined in `.github/copilot-instructions.md`:

- **No top-level statements** - Explicit `Main` method with proper class structure
- **Code-behind files** for all Razor components (`.razor.cs`)
- **Result<T> pattern** for error handling and operation results
- **Dependency injection** with constructor injection patterns
- **Comprehensive testing** with AAA pattern (Arrange, Act, Assert)
- **API versioning** with URL segments (`/api/v1.0/`)

## Configuration

### Database Connection
Configure connection strings in `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=HVO;Trusted_Connection=true;"
  }
}
```

### Environment-Specific Settings
- `appsettings.json` - Base configuration
- `appsettings.Development.json` - Development overrides
- `appsettings.Production.json` - Production settings

## Contributing

1. **Follow coding standards** as defined in `.github/copilot-instructions.md`
2. **Write comprehensive tests** for new features
3. **Use Result<T> pattern** for operations that can fail
4. **Create code-behind files** for Blazor components
5. **Update documentation** for API changes

### Pull Request Process
1. Create a feature branch from `main`
2. Implement changes with appropriate tests
3. Ensure all tests pass: `dotnet test`
4. Submit pull request with clear description

## License

This project is part of the Hualapai Valley Observatory automation system.

## Support

For questions or issues, please contact the HVO development team or create an issue in the Azure DevOps repository.