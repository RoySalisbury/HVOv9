using System;
using HVO.SkyMonitorV5.RPi.Data;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.SkyMonitorV5.RPi.Benchmarks.Infrastructure;

internal sealed class BenchmarkServiceScopeFactory : IServiceScopeFactory
{
    private readonly IStarRepository _starRepository;

    public BenchmarkServiceScopeFactory(IStarRepository starRepository)
    {
        _starRepository = starRepository ?? throw new ArgumentNullException(nameof(starRepository));
    }

    public IServiceScope CreateScope() => new BenchmarkServiceScope(_starRepository);

    private sealed class BenchmarkServiceScope : IServiceScope, IServiceProvider
    {
        private readonly IStarRepository _starRepository;

        public BenchmarkServiceScope(IStarRepository starRepository)
        {
            _starRepository = starRepository;
            ServiceProvider = this;
        }

        public IServiceProvider ServiceProvider { get; }

        public object? GetService(Type serviceType)
            => serviceType == typeof(IStarRepository) ? _starRepository : null;

        public void Dispose()
        {
            if (_starRepository is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
