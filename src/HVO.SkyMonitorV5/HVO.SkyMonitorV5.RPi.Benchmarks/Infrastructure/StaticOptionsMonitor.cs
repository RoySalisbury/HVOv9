using System;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Benchmarks.Infrastructure;

internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    where T : class
{
    private readonly T _value;

    public StaticOptionsMonitor(T value)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public T CurrentValue => _value;

    public T Get(string? name) => _value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
