using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Infrastructure;

internal static class LoggerExtensions
{
    public static bool TryLogOperationCanceled(this ILogger logger, Exception exception, CancellationToken cancellationToken, string message, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is not OperationCanceledException && exception is not TaskCanceledException)
        {
            return false;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        logger.LogDebug(exception, message, args);
        return true;
    }
}
