using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace HVO.SkyMonitorV5.RPi.Infrastructure;

/// <summary>
/// Utility methods for cancellation-aware waits that avoid throwing OperationCanceledException during normal shutdown.
/// </summary>
public static class CancellationTokenHelpers
{
    public static async Task<bool> DelayWithoutThrowAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return !cancellationToken.IsCancellationRequested;
        }

        if (!cancellationToken.CanBeCanceled)
        {
            await Task.Delay(delay).ConfigureAwait(false);
            return true;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var cancellationSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = cancellationToken.Register(static state =>
        {
            var source = (TaskCompletionSource<bool>)state!;
            source.TrySetResult(false);
        }, cancellationSource);

        var delayTask = Task.Delay(delay);
        var completed = await Task.WhenAny(delayTask, cancellationSource.Task).ConfigureAwait(false);

        if (completed == delayTask)
        {
            await delayTask.ConfigureAwait(false);
            return true;
        }

        return false;
    }

    public static async Task<bool> WaitForNextTickWithoutThrowAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return await timer.WaitForNextTickAsync().ConfigureAwait(false);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var waitTask = timer.WaitForNextTickAsync().AsTask();
        if (waitTask.IsCompleted)
        {
            return await waitTask.ConfigureAwait(false);
        }

        var cancellationSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = cancellationToken.Register(static state =>
        {
            var source = (TaskCompletionSource<bool>)state!;
            source.TrySetResult(false);
        }, cancellationSource);

        var completed = await Task.WhenAny(waitTask, cancellationSource.Task).ConfigureAwait(false);

        if (completed == waitTask)
        {
            return await waitTask.ConfigureAwait(false);
        }

        return false;
    }

    public static async ValueTask<bool> WaitToReadWithoutThrowAsync<T>(ChannelReader<T> reader, CancellationToken cancellationToken)
    {
        try
        {
            if (!cancellationToken.CanBeCanceled)
            {
                return await reader.WaitToReadAsync().ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            var waitTask = reader.WaitToReadAsync().AsTask();
            if (waitTask.IsCompleted)
            {
                if (waitTask.IsCanceled)
                {
                    return false;
                }

                return await waitTask.ConfigureAwait(false);
            }

            var cancellationSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var registration = cancellationToken.Register(static state =>
            {
                var source = (TaskCompletionSource<bool>)state!;
                source.TrySetResult(false);
            }, cancellationSource);

            var completed = await Task.WhenAny(waitTask, cancellationSource.Task).ConfigureAwait(false);

            if (completed == waitTask)
            {
                if (waitTask.IsCanceled)
                {
                    return false;
                }

                return await waitTask.ConfigureAwait(false);
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
