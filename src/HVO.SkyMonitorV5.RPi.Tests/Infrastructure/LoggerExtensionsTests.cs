using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Infrastructure;

[TestClass]
public class LoggerExtensionsTests
{
    [TestMethod]
    public void TryLogOperationCanceled_ReturnsTrueAndLogsDebug_WhenCancellationRequested()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var logger = new TestLogger();
        var exception = new OperationCanceledException(cts.Token);

        var result = logger.TryLogOperationCanceled(exception, cts.Token, "Cancelled {Operation}", "ingestion");

        Assert.IsTrue(result);
        Assert.AreEqual(1, logger.Entries.Count);
        var entry = logger.Entries[0];
        Assert.AreEqual(LogLevel.Debug, entry.LogLevel);
        Assert.AreEqual(exception, entry.Exception);
        StringAssert.Contains(entry.Message, "Cancelled ingestion");
    }

    [TestMethod]
    public void TryLogOperationCanceled_ReturnsFalse_WhenTokenNotCancelled()
    {
        using var cts = new CancellationTokenSource();
        var logger = new TestLogger();
        var exception = new OperationCanceledException();

        var result = logger.TryLogOperationCanceled(exception, cts.Token, "Cancelled");

        Assert.IsFalse(result);
        Assert.AreEqual(0, logger.Entries.Count);
    }

    [TestMethod]
    public void TryLogOperationCanceled_ReturnsFalse_WhenExceptionIsNotCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var logger = new TestLogger();
        var exception = new InvalidOperationException("boom");

        var result = logger.TryLogOperationCanceled(exception, cts.Token, "Cancelled");

        Assert.IsFalse(result);
        Assert.AreEqual(0, logger.Entries.Count);
    }

    private sealed class TestLogger : ILogger
    {
        public List<TestLogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new TestLogEntry(logLevel, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record TestLogEntry(LogLevel LogLevel, string Message, Exception? Exception);
}
