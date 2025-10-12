# Blazor Component Development Best Practices - HVOv9

## Overview
This document outlines Microsoft's recommended best practices for Blazor component development, specifically for the HVOv9 project. These practices ensure maintainable, testable, and performant Blazor components.

## Core Principles

### 1. **Separation of Concerns**
Each Blazor component should be split into distinct files based on responsibility:

- **`.razor`** - Markup and structure only
- **`.razor.cs`** - C# logic and event handlers  
- **`.razor.css`** - Component-specific styling (automatically scoped)
- **`.razor.js`** - Component-specific JavaScript (optional, automatically scoped)

### 2. **No Inline Code Blocks**
**NEVER** include inline code in Razor markup:

❌ **Don't Do This:**
```razor
<div>
    <style>
        .my-button { color: red; }
    </style>
    
    <script>
        function myFunction() { }
    </script>
    
    @code {
        private string message = "Hello";
    }
</div>
```

✅ **Do This Instead:**
```
ComponentName.razor      # Markup only
ComponentName.razor.cs   # All C# code
ComponentName.razor.css  # All styling
ComponentName.razor.js   # All JavaScript (if needed)
```

## Component File Structure

### Standard Component Pattern
```
Components/Pages/MyComponent.razor
Components/Pages/MyComponent.razor.cs
Components/Pages/MyComponent.razor.css
Components/Pages/MyComponent.razor.js  (optional)
```

### Example Implementation

#### `MyComponent.razor` (Markup Only)
```razor
@page "/my-component"
@rendermode @(new InteractiveServerRenderMode(prerender: false))

<PageTitle>My Component - HVOv9</PageTitle>

<div class="component-container">
    <div class="header-section">
        <h2 class="component-title">@Title</h2>
        <button class="action-button" @onclick="HandleClick">
            <i class="bi bi-play me-2"></i>
            @ButtonText
        </button>
    </div>
    
    <div class="content-section">
        @if (IsLoading)
        {
            <div class="loading-spinner">
                <i class="bi bi-hourglass-split"></i>
                Loading...
            </div>
        }
        else
        {
            <div class="data-display">
                @foreach (var item in Items)
                {
                    <div class="item-card">
                        <h4>@item.Name</h4>
                        <p>@item.Description</p>
                    </div>
                }
            </div>
        }
    </div>
</div>
```

#### `MyComponent.razor.cs` (Code-Behind)
```csharp
using Microsoft.AspNetCore.Components;

namespace HVO.WebSite.ProjectName.Components.Pages;

public partial class MyComponent : ComponentBase
{
    [Parameter] public string Title { get; set; } = "Default Title";
    
    private bool IsLoading { get; set; } = true;
    private string ButtonText { get; set; } = "Click Me";
    private List<MyItem> Items { get; set; } = new();

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    private async Task HandleClick()
    {
        IsLoading = true;
        StateHasChanged();
        
        await Task.Delay(1000); // Simulate work
        ButtonText = "Clicked!";
        
        IsLoading = false;
        StateHasChanged();
    }

    private async Task LoadDataAsync()
    {
        // Simulate data loading
        await Task.Delay(500);
        
        Items = new List<MyItem>
        {
            new() { Name = "Item 1", Description = "First item" },
            new() { Name = "Item 2", Description = "Second item" }
        };
        
        IsLoading = false;
        StateHasChanged();
    }

    private class MyItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
```

#### `MyComponent.razor.css` (Scoped Styling)
```css
/* Scoped CSS - automatically scoped to this component only */

.component-container {
    max-width: 1200px;
    margin: 0 auto;
    padding: 20px;
}

.header-section {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 20px;
    padding: 20px;
    background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
    border-radius: 15px;
    color: white;
}

.component-title {
    margin: 0;
    font-weight: 600;
}

.action-button {
    background: linear-gradient(135deg, #28a745, #20c997);
    border: none;
    border-radius: 10px;
    color: white;
    padding: 10px 20px;
    font-weight: 600;
    transition: transform 0.2s ease;
}

.action-button:hover {
    transform: translateY(-2px);
    box-shadow: 0 4px 8px rgba(0, 0, 0, 0.2);
}

.content-section {
    background: white;
    border-radius: 15px;
    padding: 20px;
    box-shadow: 0 4px 20px rgba(0, 0, 0, 0.1);
}

.loading-spinner {
    text-align: center;
    padding: 40px;
    color: #6c757d;
}

.loading-spinner i {
    font-size: 2rem;
    margin-bottom: 10px;
    animation: spin 1s linear infinite;
}

.data-display {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
    gap: 20px;
}

.item-card {
    background: #f8f9fa;
    border-radius: 10px;
    padding: 15px;
    border-left: 4px solid #007bff;
}

.item-card h4 {
    margin-top: 0;
    color: #495057;
}

.item-card p {
    margin-bottom: 0;
    color: #6c757d;
}

@keyframes spin {
    from { transform: rotate(0deg); }
    to { transform: rotate(360deg); }
}

/* Responsive Design */
@media (max-width: 768px) {
    .header-section {
        flex-direction: column;
        gap: 15px;
        text-align: center;
    }
    
    .component-container {
        padding: 10px;
    }
    
    .data-display {
        grid-template-columns: 1fr;
    }
}
```

#### `MyComponent.razor.js` (Scoped JavaScript - Optional)
```javascript
// Scoped JavaScript - automatically isolated to this component
export function initializeComponent(element) {
    console.log('Component initialized:', element);
    
    // Component-specific DOM manipulation
    const buttons = element.querySelectorAll('.action-button');
    buttons.forEach(button => {
        button.addEventListener('mouseenter', () => {
            button.style.transform = 'scale(1.05)';
        });
        
        button.addEventListener('mouseleave', () => {
            button.style.transform = 'scale(1)';
        });
    });
}

export function cleanupComponent(element) {
    console.log('Component cleanup:', element);
    // Cleanup any event listeners or resources
}

// Component-specific utility functions
export function highlightElement(elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.classList.add('highlight');
        setTimeout(() => {
            element.classList.remove('highlight');
        }, 2000);
    }
}
```

## Key Benefits

### 1. **Automatic CSS Scoping**
- Blazor automatically generates unique CSS selectors (e.g., `.my-button[b-abc123]`)
- Prevents style conflicts between components
- No need for complex naming conventions or CSS-in-JS

### 2. **Automatic JavaScript Scoping**
- JavaScript files are treated as ES6 modules
- Automatic loading/unloading with component lifecycle
- No global namespace pollution

### 3. **Better Maintainability**
- Clear separation of concerns
- Easier to find and modify specific aspects of components
- Better code organization and readability

### 4. **Enhanced Testability**
- Code-behind classes can be unit tested independently
- Mock dependencies easily in code-behind
- Separation makes components more testable

### 5. **Performance Benefits**
- CSS and JS are only loaded when components are used
- Better caching strategies
- Smaller initial bundle sizes

## Migration Guidelines

### From Inline Styles to Scoped CSS
1. Create `.razor.css` file alongside component
2. Move all `<style>` content to scoped CSS file
3. Remove `<style>` blocks from Razor markup
4. Remove any CSS references from App.razor or layout files

### From Inline Scripts to Scoped JavaScript
1. Create `.razor.js` file alongside component
2. Convert inline scripts to ES6 module exports
3. Remove `<script>` blocks from Razor markup
4. Use IJSRuntime to interact with scoped JS functions

### From Inline Code to Code-Behind
1. Create `.razor.cs` file alongside component
2. Move all `@code` blocks to code-behind class
3. Make class `partial` and inherit from `ComponentBase`
4. Remove `@code` blocks from Razor markup

## Common Patterns

### Component with Service Dependencies
```csharp
public partial class DataComponent : ComponentBase
{
    [Inject] private IMyService MyService { get; set; } = default!;
    [Inject] private ILogger<DataComponent> Logger { get; set; } = default!;
    
    private List<DataItem> items = new();
    
    protected override async Task OnInitializedAsync()
    {
        try
        {
            var result = await MyService.GetDataAsync();
            if (result.IsSuccess)
            {
                items = result.Value;
            }
            else
            {
                Logger.LogError("Failed to load data: {Error}", result.Exception?.Message);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading component data");
        }
    }
}
```

### Component with Parameters and Events
```csharp
public partial class EditableComponent : ComponentBase
{
    [Parameter] public string InitialValue { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> OnValueChanged { get; set; }
    
    private string currentValue = string.Empty;
    
    protected override void OnParametersSet()
    {
        currentValue = InitialValue;
    }
    
    private async Task HandleValueChange(ChangeEventArgs e)
    {
        currentValue = e.Value?.ToString() ?? string.Empty;
        await OnValueChanged.InvokeAsync(currentValue);
    }
}
```

## Integration with HVOv9 Patterns

### Using Result<T> Pattern
```csharp
public partial class ServiceComponent : ComponentBase
{
    [Inject] private IMyService MyService { get; set; } = default!;
    
    private async Task HandleOperation()
    {
        var result = await MyService.PerformOperationAsync();
        
        if (result.IsSuccess)
        {
            // Handle success
            AddNotification("Success", "Operation completed successfully", NotificationType.Success);
        }
        else
        {
            // Handle failure
            AddNotification("Error", result.Exception?.Message ?? "Operation failed", NotificationType.Error);
        }
    }
}
```

This architecture provides a clean, maintainable, and performant foundation for all Blazor components in the HVOv9 project while following Microsoft's recommended best practices.
