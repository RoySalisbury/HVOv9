# MSTest Testing Framework Standardization

This document outlines the standardization of all test projects in the HVOv9 solution to use MSTest framework exclusively.

## Overview

All test projects have been standardized to use **MSTest** instead of xUnit for consistency and maintainability.

## Standardized Test Projects

### ✅ Completed Projects
- **HVO.Iot.Devices.Tests** - Already using MSTest with comprehensive dependency injection setup
- **HVO.WebSite.Playground.Tests** - Successfully converted to MSTest

### 🔄 Conversion in Progress
- **HVO.WebSite.RoofControllerV4.Tests** - Partially converted, requires manual syntax fixes

## Project Configuration

### Package References
All test projects now use:
```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
<PackageReference Include="MSTest" Version="3.6.4" />
<PackageReference Include="Moq" Version="4.20.72" />
```

### Using Statements
Global using statement for MSTest:
```xml
<ItemGroup>
  <Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />
</ItemGroup>
```

## MSTest Attribute Mapping

### Class Attributes
- `[TestClass]` - Replaces xUnit's class fixture pattern
- Test classes no longer implement `IClassFixture<T>`

### Method Attributes
- `[TestMethod]` - Replaces xUnit's `[Fact]` and `[Theory]`
- `[DataRow(...)]` - Replaces xUnit's `[InlineData(...)]`
- `[TestInitialize]` - Replaces xUnit constructor pattern
- `[TestCleanup]` - Replaces xUnit dispose pattern

### Assertion Methods
| xUnit | MSTest |
|-------|--------|
| `Assert.Equal(expected, actual)` | `Assert.AreEqual(expected, actual)` |
| `Assert.NotEqual(expected, actual)` | `Assert.AreNotEqual(expected, actual)` |
| `Assert.True(condition)` | `Assert.IsTrue(condition)` |
| `Assert.False(condition)` | `Assert.IsFalse(condition)` |
| `Assert.NotNull(value)` | `Assert.IsNotNull(value)` |
| `Assert.Null(value)` | `Assert.IsNull(value)` |
| `Assert.Contains(item, collection)` | `Assert.IsTrue(collection.Contains(item))` |
| `Assert.Throws<T>(() => ...)` | `Assert.ThrowsException<T>(() => ...)` |

## Test Initialization Patterns

### Old xUnit Pattern
```csharp
public class MyTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;
    
    public MyTests(TestFixture fixture)
    {
        _fixture = fixture;
    }
}
```

### New MSTest Pattern
```csharp
[TestClass]
public class MyTests
{
    private TestFixture _fixture = null!;
    
    [TestInitialize]
    public void TestInitialize()
    {
        _fixture = new TestFixture();
    }
    
    [TestCleanup]
    public void TestCleanup()
    {
        _fixture?.Dispose();
    }
}
```

## Dependency Injection Integration

### GPIO Controller Example (Best Practice)
```csharp
[TestClass]
public class MockGpioControllerTests : IDisposable
{
    private ServiceProvider _serviceProvider = null!;
    private IGpioController _gpioController = null!;
    
    [TestInitialize]
    public void TestInitialize()
    {
        _serviceProvider = GpioTestConfiguration.CreateMockGpioServiceProvider();
        _gpioController = _serviceProvider.GetRequiredService<IGpioController>();
    }
    
    [TestCleanup]
    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
```

## Special Considerations

### bUnit TestContext Conflict
When using bUnit for Blazor component testing:
```csharp
[TestClass]
public class ComponentTests : Bunit.TestContext  // Explicitly specify bUnit.TestContext
{
    // Component tests
}
```

### ASP.NET Integration Tests
```csharp
[TestClass]
public class IntegrationTests
{
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    
    [TestInitialize]
    public void TestInitialize()
    {
        _factory = new TestWebApplicationFactory();
        _client = _factory.CreateClient();
    }
    
    [TestCleanup]
    public void TestCleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }
}
```

## Running Tests

### Individual Project
```bash
dotnet test HVO.Iot.Devices.Tests
```

### All Test Projects
```bash
dotnet test
```

### With Filters
```bash
dotnet test --filter "TestCategory=Unit"
dotnet test --filter "DisplayName~MockGpio"
```

## Benefits of MSTest Standardization

1. **Consistency**: Single testing framework across the entire solution
2. **Maintainability**: Unified test patterns and practices
3. **Performance**: Better integration with Visual Studio and CI/CD pipelines
4. **Features**: Rich assertion library and extensive tooling support
5. **Dependency Injection**: Seamless integration with .NET DI container

## Migration Status

### Completed ✅
- Package references updated
- Global using statements configured  
- Basic attribute conversion completed
- Dependency injection patterns established

### Remaining Tasks 🔄
- Manual syntax fixes for RoofControllerV4.Tests
- Verification of all assertion conversions
- Test execution validation

## Best Practices

1. **Use Dependency Injection**: Follow the GPIO controller pattern for testable code
2. **Proper Disposal**: Always implement cleanup in `[TestCleanup]` methods
3. **Clear Naming**: Use descriptive test method names
4. **Arrange-Act-Assert**: Maintain clear test structure
5. **Data-Driven Tests**: Use `[DataRow]` for parameterized tests

This standardization ensures all tests follow consistent patterns and can be easily maintained and extended as the solution grows.
