using System;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Tests.TestHelpers;

internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>, IDisposable where T : class
{
    private T _value;

    public TestOptionsMonitor(T value)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public T CurrentValue => _value;

    public T Get(string? name) => _value;

    public IDisposable OnChange(Action<T, string?> listener) => this;

    public void Update(T value)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public void Dispose()
    {
    }
}
