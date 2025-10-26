using System;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Infrastructure.Logging;

internal sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly string _filePath;
    private readonly long _maxFileSizeBytes;
    private readonly int _maxRetainedFiles;
    private readonly LogLevel _minimumLevel;

    public RollingFileLoggerProvider(string directoryPath, string fileName, long maxFileSizeBytes, int maxRetainedFiles, LogLevel minimumLevel)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        if (maxFileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileSizeBytes));
        }

        if (maxRetainedFiles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetainedFiles));
        }

        Directory.CreateDirectory(directoryPath);
        _filePath = Path.Combine(directoryPath, fileName);
        _maxFileSizeBytes = maxFileSizeBytes;
        _maxRetainedFiles = maxRetainedFiles;
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(categoryName, this);

    public void Dispose()
    {
    }

    private void WriteMessage(string categoryName, LogLevel logLevel, EventId eventId, string message, Exception? exception)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var builder = new StringBuilder()
            .Append(timestamp)
            .Append(' ')
            .Append('[')
            .Append(logLevel)
            .Append(']')
            .Append(' ')
            .Append(categoryName)
            .Append('[')
            .Append(eventId.Id)
            .Append(']')
            .Append(' ')
            .Append(message);

        if (exception is not null)
        {
            builder.AppendLine().Append(exception);
        }

        var line = builder.ToString();
        lock (_sync)
        {
            RollIfNeeded();
            File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    private void RollIfNeeded()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        var info = new FileInfo(_filePath);
        if (info.Length < _maxFileSizeBytes)
        {
            return;
        }

        for (var index = _maxRetainedFiles - 1; index >= 1; index--)
        {
            var source = GetArchivePath(index);
            var destination = GetArchivePath(index + 1);

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }

        var firstArchive = GetArchivePath(1);
        if (File.Exists(firstArchive))
        {
            File.Delete(firstArchive);
        }

        File.Move(_filePath, firstArchive);
    }

    private string GetArchivePath(int index) => $"{_filePath}.{index:D2}";

    private sealed class RollingFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly RollingFileLoggerProvider _provider;

        public RollingFileLogger(string categoryName, RollingFileLoggerProvider provider)
        {
            _categoryName = categoryName;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _provider._minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);
            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            _provider.WriteMessage(_categoryName, logLevel, eventId, message, exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
