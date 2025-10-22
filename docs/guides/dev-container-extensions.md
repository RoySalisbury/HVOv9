# HVO Dev Container Extensions Configuration

## Overview
The HVOv9 dev container now includes comprehensive extensions for .NET development, specifically optimized for the HVO project requirements.

## Extensions Added

### Core .NET Development
- **ms-dotnettools.csdevkit**: C# Dev Kit (includes IntelliSense, debugging, testing)
- **ms-dotnettools.csharp**: C# language support  
- **ms-dotnettools.vscode-dotnet-runtime**: .NET Runtime support

### ASP.NET Core and Blazor Development
- **ms-dotnettools.blazorwasm-companion**: Blazor WebAssembly debugging support

### Testing and Diagnostics
- **ms-dotnettools.vscodeintellicode-csharp**: AI-assisted IntelliCode for C#

### Database and Entity Framework
- **ms-mssql.mssql**: SQL Server support for Entity Framework development

### Container and Development Tools
- **ms-vscode.vscode-json**: Enhanced JSON support for appsettings
- **ms-vscode.powershell**: PowerShell support for scripts

### Git and Collaboration
- **GitHub.vscode-pull-request-github**: GitHub PR integration
- **GitHub.remotehub**: GitHub remote repository support
- **ms-vsliveshare.vsliveshare**: Live Share for collaboration

### Azure and Cloud Development
- **ms-azuretools.vscode-azure-mcp-server**: Azure MCP integration
- **ms-azuretools.vscode-docker**: Docker support for containerized apps

### IoT and Hardware Development (HVO-specific)
- **ms-vscode.cpptools**: C/C++ for hardware interface development

### Productivity Extensions
- **ms-vscode.vscode-markdown**: Enhanced Markdown support for documentation
- **redhat.vscode-xml**: XML support for project files
- **ms-python.python**: Python support for analysis scripts

## Development Settings Added

### .NET Configuration
- Default solution: `src/HVOv9.DevContainer.slnx`
- Enhanced completion with unimported namespaces
- Unit test settings path configured

### C# Development
- Semantic highlighting enabled
- OmniSharp error logging enabled
- Dotnet install warnings suppressed

### Editor Configuration
- 2-space indentation
- Format on save enabled
- Auto-organize imports and fix issues

### Terminal Configuration
- Default bash profile with login shell
- Environment variables loaded automatically

### File Associations
- `.razor` files properly associated
- `.cshtml` files properly associated  
- `.runsettings` files treated as XML

## Port Forwarding
Added support for common development ports:
- 5136: HTTP development server
- 7151: HTTPS development server
- 5000: Alternative ASP.NET Core HTTP
- 5001: Alternative ASP.NET Core HTTPS
- 8080: Common service development port
- 3000: Common frontend development port

## Benefits

✅ **Complete .NET Development Stack**: All essential tools for C# and ASP.NET Core development  
✅ **Blazor Support**: Full support for HVO's Blazor Server applications  
✅ **Database Integration**: Entity Framework and SQL Server tools ready  
✅ **Hardware Development**: C/C++ support for IoT device integration  
✅ **Azure Integration**: Cloud development tools for HVO's Azure services  
✅ **Testing Framework**: Comprehensive testing and debugging support  
✅ **Auto-Configuration**: All settings optimized for HVO project structure  

## Next Steps

When you rebuild the dev container:
1. All extensions will be automatically installed
2. IntelliSense and debugging will work immediately
3. C# Dev Kit will provide enhanced project management
4. Testing framework will be fully integrated
5. All development ports will be available

The dev container will now provide a complete, professional .NET development environment!