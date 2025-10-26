# HVO.SourceGenerators - Build-Time Code Generation

Roslyn source generator that automatically creates discriminated union boilerplate for types annotated with `[NamedOneOf]`.

## 📦 Package Information

- **Target Framework**: netstandard2.0 (Roslyn requirement)
- **Namespace**: `HVO`
- **Type**: Roslyn Incremental Source Generator
- **Language Version**: Latest C#

## 🎯 Purpose

Eliminates repetitive boilerplate when implementing discriminated unions with the OneOf pattern by:
- Auto-generating type-safe factory methods for each union state
- Creating pattern matching helper methods
- Providing `ToString()` implementations
- Reducing manual coding errors in state machine implementations

## 📁 Structure

```
HVO.SourceGenerators/
├── NamedOneOfGenerator.cs      # Main incremental generator
├── NamedOneOfReceiver.cs       # Syntax receiver for attribute detection
└── HVO.SourceGenerators.csproj # Roslyn analyzer project
```

## 🔑 Key Features

### Incremental Source Generation
- **Performance** - Only regenerates when relevant code changes
- **IDE Integration** - Real-time IntelliSense for generated code
- **Build-Time** - No runtime reflection overhead

### Automatic Code Generation
For a class annotated with `[NamedOneOf]`, generates:
- **Factory methods** - Type-safe constructors for each state
- **Pattern matching** - `Match()` methods with exhaustive checking
- **Type checks** - `Is<TState>()` convenience methods
- **String representation** - Meaningful `ToString()` output

## 🎓 Usage

### 1. Define Your Discriminated Union

```csharp
using HVO;

namespace MyApp.Models
{
    [NamedOneOf]
    public partial class RoofState : IOneOf
    {
        // Define possible states as nested types
        public record Closed;
        public record Opening(int PercentComplete);
        public record Open;
        public record Closing(int PercentComplete);
        public record Faulted(string ErrorMessage);
    }
}
```

### 2. Generated Code (Automatic)

The source generator produces:
```csharp
// Generated in RoofState.g.cs
public partial class RoofState
{
    // Factory methods
    public static RoofState CreateClosed() 
        => new RoofState { Value = new Closed() };
        
    public static RoofState CreateOpening(int percentComplete) 
        => new RoofState { Value = new Opening(percentComplete) };
        
    public static RoofState CreateOpen() 
        => new RoofState { Value = new Open() };
        
    // Pattern matching
    public TResult Match<TResult>(
        Func<Closed, TResult> closed,
        Func<Opening, TResult> opening,
        Func<Open, TResult> open,
        Func<Closing, TResult> closing,
        Func<Faulted, TResult> faulted)
    {
        return Value switch
        {
            Closed v => closed(v),
            Opening v => opening(v),
            Open v => open(v),
            Closing v => closing(v),
            Faulted v => faulted(v),
            _ => throw new InvalidOperationException("Unknown state")
        };
    }
    
    // Type checks
    public bool IsClosed() => Value is Closed;
    public bool IsOpening() => Value is Opening;
    public bool IsOpen() => Value is Open;
    
    // ToString
    public override string ToString()
    {
        return Value switch
        {
            Closed => "Closed",
            Opening o => $"Opening ({o.PercentComplete}%)",
            Open => "Open",
            Closing c => $"Closing ({c.PercentComplete}%)",
            Faulted f => $"Faulted: {f.ErrorMessage}",
            _ => "Unknown"
        };
    }
}
```

### 3. Use Generated Factory Methods

```csharp
public class RoofController
{
    private RoofState _state = RoofState.CreateClosed();
    
    public async Task OpenRoofAsync()
    {
        _state = RoofState.CreateOpening(0);
        
        for (int pct = 0; pct <= 100; pct += 10)
        {
            _state = RoofState.CreateOpening(pct);
            await Task.Delay(1000);
        }
        
        _state = RoofState.CreateOpen();
    }
    
    public string GetStatusMessage()
    {
        return _state.Match(
            closed: _ => "Roof is closed",
            opening: o => $"Roof opening: {o.PercentComplete}%",
            open: _ => "Roof is open",
            closing: c => $"Roof closing: {c.PercentComplete}%",
            faulted: f => $"ERROR: {f.ErrorMessage}"
        );
    }
    
    public bool CanStartObserving()
    {
        return _state.IsOpen();
    }
}
```

## ⚙️ Integration

### Add to Projects
The generator is automatically available to any project that references `HVO`:

```xml
<ItemGroup>
  <ProjectReference Include="..\HVO\HVO.csproj" OutputItemType="Analyzer" />
  <!-- OutputItemType="Analyzer" ensures source generator runs -->
</ItemGroup>
```

### View Generated Code
In Visual Studio or VS Code:
1. Expand project dependencies
2. Find **Analyzers → HVO.SourceGenerators → NamedOneOfGenerator**
3. View generated `.g.cs` files

Or check: `obj/Debug/net9.0/generated/HVO.SourceGenerators/`

## 🧪 Testing Generated Code

```csharp
[TestMethod]
public void RoofState_FactoryMethods_CreateCorrectStates()
{
    var closed = RoofState.CreateClosed();
    Assert.IsTrue(closed.IsClosed());
    
    var opening = RoofState.CreateOpening(50);
    Assert.IsTrue(opening.IsOpening());
    
    var faulted = RoofState.CreateFaulted("Motor jammed");
    Assert.IsTrue(faulted.IsFaulted());
}

[TestMethod]
public void RoofState_Match_ExecutesCorrectBranch()
{
    var state = RoofState.CreateOpening(75);
    
    var result = state.Match(
        closed: _ => "CLOSED",
        opening: o => $"OPENING-{o.PercentComplete}",
        open: _ => "OPEN",
        closing: c => $"CLOSING-{c.PercentComplete}",
        faulted: f => $"FAULTED-{f.ErrorMessage}"
    );
    
    Assert.AreEqual("OPENING-75", result);
}

[TestMethod]
public void RoofState_ToString_ReturnsReadableOutput()
{
    var state = RoofState.CreateClosing(30);
    Assert.AreEqual("Closing (30%)", state.ToString());
}
```

## 🏗️ How It Works

### 1. Syntax Analysis
```csharp
// NamedOneOfReceiver finds types with [NamedOneOf]
predicate: static (node, _) => node is TypeDeclarationSyntax tds
    && tds.AttributeLists.Count > 0
    && tds.Modifiers.Any(m => m.ValueText == "partial")
```

### 2. Semantic Analysis
```csharp
// NamedOneOfGenerator inspects type symbol
var attr = symbol.GetAttributes()
    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "HVO.NamedOneOfAttribute");
```

### 3. Code Emission
```csharp
// Generate partial class with factory methods, Match(), etc.
context.AddSource($"{symbol.Name}.g.cs", sourceText);
```

## ⚡ Performance

### Build Impact
- **Incremental** - Only regenerates changed types
- **Fast** - Typical generation < 50ms per type
- **Cached** - IDE caches generated code across builds

### Runtime Impact
- **Zero** - All code generated at compile time
- **No Reflection** - Direct method calls only
- **Type-Safe** - Compile-time verification

## 🔗 Dependencies

- **Microsoft.CodeAnalysis.CSharp** - Roslyn syntax/semantic APIs (PrivateAssets)
- **Microsoft.CodeAnalysis.Analyzers** - Analyzer best practices (PrivateAssets)

## 📚 Used By

Every project using the `[NamedOneOf]` pattern:
- `HVO.RoofControllerV4.Common` - RoofState, SafetyState
- `HVO.SkyMonitorV5.RPi` - MonitorState, ProcessingState
- `HVO.NinaClient` - ConnectionState
- `HVO.Iot.Devices` - DeviceState

## 🎨 Design Rationale

### Why Source Generators?
- **Type Safety** - Compile-time validation vs. runtime reflection
- **Performance** - Zero runtime cost
- **IntelliSense** - IDE autocomplete for generated methods
- **Debuggability** - Step through generated code

### Why Discriminated Unions?
- **State Machines** - Model complex equipment states explicitly
- **Exhaustive Matching** - Compiler ensures all cases handled
- **Data + State** - Store state-specific data (e.g., error messages, progress)
- **Refactoring Safety** - Adding states breaks builds until handled

## 🔄 Future Enhancements

- [ ] Add JSON serialization support for generated types
- [ ] Generate async `Match()` overloads
- [ ] Support generic state types
- [ ] Add diagnostic analyzers for common mistakes
- [ ] Generate XML documentation comments

## 📖 Related Documentation

- [Roslyn Source Generators](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)
- [Incremental Generators](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md)
- [HVO OneOf Pattern](../HVO/README.md#oneoft-discriminated-unions)
- [Discriminated Unions in C#](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/functional/pattern-matching)
