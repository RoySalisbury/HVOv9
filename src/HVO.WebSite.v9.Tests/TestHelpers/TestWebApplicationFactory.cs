using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HVO.DataModels.Data;

namespace HVO.WebSite.v9.Tests.TestHelpers;

/// <summary>
/// Custom WebApplicationFactory for integration testing of v9 site.
/// Replaces SQL Server with EF Core InMemory provider to avoid external dependencies.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<HVO.WebSite.v9.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove EF Core SQL Server registrations
            var efDescriptors = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<HvoDbContext>) ||
                d.ServiceType == typeof(HvoDbContext) ||
                d.ServiceType == typeof(DbContextOptions) ||
                (d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>)) ||
                (d.ImplementationType?.FullName?.Contains("EntityFramework") == true) ||
                (d.ImplementationType?.FullName?.Contains("SqlServer") == true) ||
                (d.ServiceType.FullName?.Contains("EntityFramework") == true) ||
                (d.ServiceType.FullName?.Contains("SqlServer") == true))
                .ToList();

            foreach (var descriptor in efDescriptors)
            {
                services.Remove(descriptor);
            }

            // Safety: remove stray DbContext registrations
            var dbContextDescriptors = services.Where(d =>
                d.ServiceType.Name.Contains("DbContext") ||
                (d.ImplementationType?.Name.Contains("DbContext") == true))
                .ToList();

            foreach (var descriptor in dbContextDescriptors)
            {
                services.Remove(descriptor);
            }

            // Add in-memory database for testing with unique name per factory
            services.AddDbContext<HvoDbContext>(options =>
            {
                options.UseInMemoryDatabase($"TestDb-{Guid.NewGuid()}");
                options.EnableSensitiveDataLogging();
            });
        });

        builder.UseEnvironment("Testing");
    }
}
