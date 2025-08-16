# HVO.WebSite.v9.Tests

MSTest-based test project for the HVO v9 website. It mirrors the Playground test architecture but includes only integration tests for built-in health endpoints.

## Purpose

- Validate that the v9 site exposes and serves health endpoints correctly:
  - `/health` (detailed report)
  - `/health/ready` (database-tagged readiness)
  - `/health/live` (liveness)
- Run tests without external infrastructure by swapping SQL Server for EF Core InMemory in tests

## Technologies Used

- MSTest (test framework)
- Microsoft.AspNetCore.Mvc.Testing (test host / WebApplicationFactory)
- FluentAssertions (assertions)
- EF Core InMemory provider for test isolation

## Structure

- `Integration/HealthChecksTests.cs` — integration tests hitting the real pipeline
- `TestHelpers/TestWebApplicationFactory.cs` — custom factory replacing EF SQL registration with InMemory and setting environment to `Testing`

## Running Tests

From the repo root:

```bash
 dotnet test src/HVO.WebSite.v9.Tests/HVO.WebSite.v9.Tests.csproj -c Debug
```
