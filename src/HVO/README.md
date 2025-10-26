# HVO - Core Library

Core shared library for all HVOv9 projects, providing fundamental patterns, utilities, and abstractions used throughout the observatory software suite.

## 📦 Package Information

- **Target Framework**: .NET 9.0
- **Namespace**: `HVO`
- **Type**: Shared Library

## 🎯 Purpose

This library provides the foundational building blocks for the entire HVOv9 ecosystem:

1. **Result Pattern** - Railway-oriented programming for error handling
2. **Option Pattern** - Safer handling of potentially null values  
3. **OneOf Pattern** - Discriminated unions for type-safe alternatives
4. **Component Model Extensions** - Enhanced property change notification
5. **Astronomy Utilities** - Shared astronomy calculations and types

## 📁 Structure

```
HVO/
├── Result.cs              # Result<T> pattern for operation outcomes
├── Option.cs              # Option<T> pattern for optional values
├── IOneOf.cs             # OneOf discriminated union interface
├── NamedOneOfAttribute.cs # Attribute for named OneOf variants
├── OneOfExtensions.cs    # Extension methods for OneOf pattern
├── Astronomy/            # Shared astronomy types and utilities
└── ComponentModel/       # INotifyPropertyChanged extensions
```

## 🔑 Key Features

### Result<T> Pattern

Railway-oriented programming pattern for handling success/failure scenarios without exceptions:

```csharp
using HVO;

public Result<WeatherData> GetWeather()
{
    try
    {
        var data = FetchWeatherData();
        return Result<WeatherData>.Success(data);
    }
    catch (Exception ex)
    {
        return Result<WeatherData>.Failure(ex);
    }
}

// Usage
var result = GetWeather();
if (result.IsSuccess)
{
    Console.WriteLine($"Temperature: {result.Value.Temperature}");
}
else
{
    _logger.LogError(result.Exception, "Failed to fetch weather");
}
```

**Benefits:**
- Explicit error handling without try/catch blocks
- Forces callers to handle both success and failure paths
- Better composability than exception-based code
- Used extensively in service layers and API controllers

### Option<T> Pattern

Safer alternative to nullable references:

```csharp
public Option<Device> FindDevice(string id)
{
    var device = _devices.FirstOrDefault(d => d.Id == id);
    return device != null 
        ? Option<Device>.Some(device) 
        : Option<Device>.None();
}

// Pattern matching
var result = FindDevice("device-123");
return result switch
{
    { HasValue: true } => $"Found: {result.Value.Name}",
    _ => "Device not found"
};
```

### OneOf Discriminated Unions

Type-safe alternatives without inheritance:

```csharp
[NamedOneOf]
public class DeviceState : IOneOf
{
    public static DeviceState Ready() => new() { /* ... */ };
    public static DeviceState Busy() => new() { /* ... */ };
    public static DeviceState Error(string message) => new() { /* ... */ };
}
```

### Component Model Extensions

Enhanced `INotifyPropertyChanged` support for Blazor components and view models:

```csharp
using HVO.ComponentModel;

public class MyViewModel : INotifyPropertyChanged
{
    private string _status = string.Empty;
    
    public string Status
    {
        get => _status;
        set => this.SetProperty(ref _status, value, OnPropertyChanged);
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    private void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, e);
    }
}
```

## 🔗 Dependencies

- **Microsoft.CodeAnalysis.CSharp** - For compile-time analysis and code generation support

## 🎓 Usage Examples

### Service Layer Pattern

```csharp
using HVO;

public class WeatherService : IWeatherService
{
    private readonly ILogger<WeatherService> _logger;
    
    public async Task<Result<CurrentWeather>> GetCurrentWeatherAsync()
    {
        try
        {
            var data = await FetchFromSensorAsync();
            return Result<CurrentWeather>.Success(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve weather data");
            return Result<CurrentWeather>.Failure(ex);
        }
    }
}

// Controller usage
public class WeatherController : ControllerBase
{
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent()
    {
        var result = await _weatherService.GetCurrentWeatherAsync();
        
        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(500, new ProblemDetails 
              { 
                  Title = "Weather data unavailable",
                  Detail = result.Exception?.Message 
              });
    }
}
```

### Blazor Component Integration

```csharp
using HVO.ComponentModel;

@code {
    private DeviceStatus _status = new();
    
    protected override async Task OnInitializedAsync()
    {
        _status.PropertyChanged += (s, e) => InvokeAsync(StateHasChanged);
        
        var result = await DeviceService.GetStatusAsync();
        if (result.IsSuccess)
        {
            _status = result.Value; // Triggers UI update
        }
    }
}
```

## 📚 Design Patterns

### Railway-Oriented Programming (Result<T>)
- **Purpose**: Eliminate exception-driven control flow
- **When to Use**: Service methods, API endpoints, data access
- **Replaces**: Try/catch blocks for expected failures

### Option/Maybe Pattern
- **Purpose**: Make nullability explicit in the type system
- **When to Use**: Database lookups, configuration values, optional parameters
- **Replaces**: Nullable reference types where explicit handling is needed

### Discriminated Unions (OneOf)
- **Purpose**: Type-safe state machines and variants
- **When to Use**: Device states, command results, workflow states
- **Replaces**: Enum + separate data pattern

## ⚙️ Configuration

No configuration required - this is a pure library with no runtime dependencies.

## 🧪 Testing

Covered by integration tests in domain-specific projects:
- `HVO.Iot.Devices.Tests` - Tests IoT device Result<T> usage
- `HVO.WebSite.Playground.Tests` - Tests web service Result<T> patterns
- `HVO.RoofControllerV4.RPi.Tests` - Tests controller service patterns

## 📖 Related Documentation

- [Result<T> Pattern Best Practices](../../docs/guides/result-pattern-best-practices.md) *(if exists)*
- [Service Layer Architecture](../../docs/guides/service-layer-architecture.md) *(if exists)*
- [Error Handling Standards](../../.github/copilot-instructions.md#exception-handling-middleware)

## 🔄 Version History

- **Current**: Part of HVOv9 reorganization (October 2025)
- Moved to `src/HVO/` as core shared library
- Used by all 26 projects in the solution
