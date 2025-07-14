# HVO.WebSite.Playground

A Blazor Server web application for testing health check and weather API endpoints with both Blazor components and traditional MVC views, demonstrating different UI approaches with the same APIs.

## Features

- **Blazor Server Components**: Interactive UI with C# code-behind
- **Traditional MVC Views**: Classic web development with JavaScript
- **Health Check API**: Built-in ASP.NET Core health checks with custom JSON responses
- **Weather API**: RESTful API with URL segment versioning (`/api/v1.0/`)
- **HttpClient Integration**: Proper HttpClient configuration with IHttpClientFactory
- **Bootstrap 5**: Modern, responsive UI design
- **Real-time Updates**: Server-side Blazor with SignalR
- **Error Handling**: Comprehensive error handling and logging with global exception handler
- **Side-by-Side Comparison**: Both approaches accessing the same APIs

## Project Structure

```
HVO.WebSite.Playground/
├── Components/
│   ├── Layout/                 # Application layout components
│   │   ├── NavMenu.razor       # Navigation menu
│   │   └── MainLayout.razor    # Main layout wrapper
│   ├── Pages/                  # Blazor pages
│   │   ├── Home.razor          # Home page
│   │   └── HealthCheckBlazor.razor # Blazor health check testing page
│   └── _Imports.razor          # Global imports
├── Controllers/
│   ├── HomeController.cs       # MVC controller for views
│   └── WeatherController.cs    # Weather API controller
├── Services/
│   ├── IWeatherService.cs      # Weather service interface
│   └── WeatherService.cs       # Weather service implementation
├── Models/                     # API response models
│   ├── LatestWeatherResponse.cs
│   ├── CurrentWeatherResponse.cs
│   └── WeatherHighsLowsResponse.cs
├── Middleware/
│   └── HvoServiceExceptionHandler.cs # Global exception handler
├── Views/
│   ├── Home/
│   │   ├── Index.cshtml        # Landing page with comparison
│   │   └── HealthCheckMVC.cshtml # MVC health check testing page
│   └── Shared/
│       └── _Layout.cshtml      # MVC layout
├── wwwroot/                    # Static files
│   ├── css/                    # Stylesheets
│   └── js/                     # JavaScript files
└── Program.cs                  # Application startup
```

## Getting Started

### Prerequisites

- .NET 9.0 SDK
- Visual Studio Code or Visual Studio

### Running the Application

1. Clone or download the project
2. Open a terminal in the project directory
3. Run the application:
   ```bash
   dotnet run
   ```
4. Open your browser and navigate to the displayed URL (typically `http://localhost:5136`)

### Testing the APIs

#### Home Page
Navigate to the application root to see a comparison page:
- **URL**: `http://localhost:5136/`
- **Features**: Side-by-side comparison of MVC vs Blazor approaches

#### Direct Health Check Access
Test the health check endpoints directly:
```bash
# Main health check with detailed information
curl http://localhost:5136/health

# Ready check (includes database connectivity)
curl http://localhost:5136/health/ready

# Live check (basic application health)
curl http://localhost:5136/health/live
```

#### Direct Weather API Access
Test the weather API endpoints directly:
```bash
# Latest weather conditions
curl http://localhost:5136/api/v1.0/weather/latest

# Current weather conditions
curl http://localhost:5136/api/v1.0/weather/current

# Daily highs and lows
curl http://localhost:5136/api/v1.0/weather/highs-lows
```

#### MVC UI Testing
1. Navigate to `http://localhost:5136/Home/HealthCheckMVC`
2. Click the "🧪 Test Health Check API" button
3. View the response with traditional JavaScript/DOM manipulation

#### Blazor UI Testing
1. Navigate to `http://localhost:5136/health-check-blazor`
2. Click the "🧪 Test Health Check API" button
3. View the response with real-time C# updates via SignalR

## API Endpoints

### Health Check APIs
- **URL**: `/health` - Main health check endpoint
- **Method**: `GET`
- **Response**: JSON object with status, checks, and duration

```json
{
  "status": "Healthy",
  "checks": [
    {
      "name": "self",
      "status": "Healthy",
      "exception": null,
      "duration": "00:00:00.0001234"
    },
    {
      "name": "database",
      "status": "Healthy",
      "exception": null,
      "duration": "00:00:00.0123456"
    }
  ],
  "duration": "00:00:00.0234567"
}
```

### Weather APIs
- **Base URL**: `/api/v1.0/weather/`
- **Methods**: `GET`
- **Versioning**: URL segment versioning (v1.0)

#### Latest Weather
- **Endpoint**: `/api/v1.0/weather/latest`
- **Description**: Most recent weather conditions

#### Current Weather  
- **Endpoint**: `/api/v1.0/weather/current`
- **Description**: Current weather conditions

#### Weather Highs/Lows
- **Endpoint**: `/api/v1.0/weather/highs-lows`
- **Description**: Daily high and low temperatures

**Sample Weather Response:**
```json
{
  "timestamp": "2025-07-13T10:30:00Z",
  "machineName": "HVO-Server",
  "data": {
    "temperature": 75.2,
    "humidity": 45.8,
    "pressure": 29.92,
    "conditions": "Partly Cloudy"
  }
}
```

## Technologies Used

- **.NET 9.0**: Latest .NET framework
- **Blazor Server**: Server-side Blazor with SignalR
- **ASP.NET Core MVC**: Traditional MVC framework
- **ASP.NET Core Health Checks**: Built-in health monitoring
- **Entity Framework Core**: Database access and health checks
- **Asp.Versioning.Mvc**: API versioning support
- **Bootstrap 5**: CSS framework for responsive design
- **HttpClient**: HTTP client with factory pattern
- **Global Exception Handling**: Centralized error management
- **Vanilla JavaScript**: Modern JavaScript without jQuery dependencies
- **Fetch API**: Modern HTTP client for browser-based requests

## Architecture

The application demonstrates a comprehensive comparison between two modern web development approaches:

### 1. **Traditional MVC + JavaScript**
- **Controller**: `HomeController.cs` with action methods
- **Views**: Razor views (`.cshtml`) with server-side rendering
- **Client-Side**: Vanilla JavaScript with Fetch API
- **Communication**: Standard HTTP requests/responses
- **State Management**: Client-side DOM manipulation
- **Example**: HealthCheckMVC.cshtml demonstrates API testing with JavaScript

### 2. **Blazor Server Components**
- **Components**: Razor components (`.razor`) with C# code-behind
- **Server-Side**: C# runs on the server
- **Client-Side**: Minimal JavaScript, SignalR for communication
- **Communication**: Real-time SignalR connection
- **State Management**: Server-side state with automatic UI updates
- **Example**: HealthCheckBlazor.razor demonstrates API testing with C#

### Common Elements
- **API Layer**: Shared RESTful APIs (Health Check + Weather) with versioning
- **Dependency Injection**: IHttpClientFactory for HTTP client management
- **Error Handling**: Global exception handler with ProblemDetails
- **Service Layer**: WeatherService for business logic abstraction
- **Data Access**: Entity Framework Core with health checks
- **Styling**: Bootstrap 5 for consistent UI across both approaches

## Development

### Adding New MVC Views

1. Create a new action method in `Controllers/HomeController.cs`
2. Create a corresponding view in `Views/Home/[ActionName].cshtml`
3. Use the `_Layout.cshtml` for consistent styling
4. Add JavaScript in a `@section Scripts` block if needed

### Adding New Blazor Pages

1. Create a new `.razor` file in `Components/Pages/`
2. Add the `@page` directive with the route
3. Add `@rendermode InteractiveServer` for server-side interactivity
4. Update the navigation menu in `Components/Layout/NavMenu.razor`

### Adding New API Endpoints

1. Create a new controller in `Controllers/` (e.g., `WeatherController.cs`)
2. Add the `[ApiVersion("1.0")]` attribute for versioning
3. Use the `[Route("api/v{version:apiVersion}/[controller]")]` attribute
4. Implement service interfaces in `Services/` for business logic
5. Create response models in `Models/` for API responses

### Working with Health Checks

1. Add new health checks in `Program.cs` using `AddHealthChecks()`
2. Use tags to categorize health checks (e.g., "ready", "live", "db")
3. Access health check endpoints: `/health`, `/health/ready`, `/health/live`
4. Health checks automatically include database connectivity via Entity Framework

## Comparison Guide

| Feature | MVC + JavaScript | Blazor Server |
|---------|------------------|---------------|
| **Language** | C# + JavaScript | C# Only |
| **Execution** | Client + Server | Server |
| **State Management** | Client-side DOM | Server-side |
| **Real-time Updates** | Manual DOM updates | Automatic via SignalR |
| **Network** | HTTP Requests | SignalR Connection |
| **Development** | Traditional Web | Component-based |
| **Performance** | Lower server load | Higher server load |
| **Offline Support** | Possible | Limited |
| **API Testing** | HealthCheckMVC.cshtml | HealthCheckBlazor.razor |
| **Error Handling** | JavaScript try/catch | C# exception handling |

## Additional Features

- **Swagger/OpenAPI**: Available in development mode at `/swagger`
- **Global Exception Handler**: Centralized error handling with ProblemDetails
- **API Versioning**: URL segment versioning for future API evolution
- **Entity Framework Integration**: Database health checks and data access
- **HttpClient Factory**: Proper HTTP client lifecycle management
- **Service Layer Pattern**: Clean separation of concerns with business logic services

## License

This project is part of the Hualapai Valley Observatory (HVO) software suite for educational and observatory automation purposes.
