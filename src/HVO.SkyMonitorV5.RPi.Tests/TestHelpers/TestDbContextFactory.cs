using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace HVO.SkyMonitorV5.RPi.Tests.TestHelpers;

internal sealed class TestDbContextFactory<TContext> : IDbContextFactory<TContext>
    where TContext : DbContext
{
    private readonly Func<TContext> _factory;

    public TestDbContextFactory(Func<TContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public TContext CreateDbContext() => _factory();

    public ValueTask<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask<TContext>(_factory());
    }
}
