# Copilot Instructions for HVO.WebSite.Playground2

<!-- Use this file to provide workspace-specific custom instructions to Copilot. For more details, visit https://code.visualstudio.com/docs/copilot/copilot-customization#_use-a-githubcopilotinstructionsmd-file -->

## Project Overview
This is a Blazor Server web application project for testing API endpoints with both Blazor components and traditional MVC views.

## Technologies Used
- .NET 9.0
- Blazor Server
- ASP.NET Core MVC
- API Versioning
- Bootstrap 5
- HttpClient with IHttpClientFactory pattern

## Architecture Guidelines
- Use Server-side Blazor components for interactive UI
- Implement API versioning with URL segments (e.g., `/api/v1.0/ping`)
- Use IHttpClientFactory for HTTP client management
- Follow dependency injection patterns
- Use proper error handling and logging
- Implement responsive design with Bootstrap 5

## Code Style Preferences
- Use C# 12 features where appropriate
- Follow ASP.NET Core conventions
- Use async/await patterns for asynchronous operations
- Implement proper disposal patterns for resources
- Use meaningful variable and method names
- Include XML documentation for public APIs
