using HVO;
using Microsoft.Extensions.Logging;

namespace HVO.NinaClient.Resilience;

/// <summary>
/// Circuit breaker states
/// </summary>
public enum CircuitBreakerState
{
    Closed,    // Normal operation
    Open,      // Circuit is open, failing fast
    HalfOpen   // Testing if service has recovered
}

/// <summary>
/// Circuit breaker for fault tolerance
/// </summary>
public sealed class CircuitBreaker : IDisposable
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _timeout;
    private readonly ILogger? _logger;
    private readonly object _lock = new();
    
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount = 0;
    private DateTime _nextAttempt = DateTime.MinValue;
    private bool _disposed;

    public CircuitBreaker(int failureThreshold, TimeSpan timeout, ILogger? logger = null)
    {
        _failureThreshold = failureThreshold;
        _timeout = timeout;
        _logger = logger;
        
        _logger?.LogDebug("Circuit breaker initialized - FailureThreshold: {FailureThreshold}, Timeout: {Timeout}", 
            failureThreshold, timeout);
    }

    /// <summary>
    /// Gets the current state of the circuit breaker
    /// </summary>
    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Gets the current failure count
    /// </summary>
    public int FailureCount
    {
        get
        {
            lock (_lock)
            {
                return _failureCount;
            }
        }
    }

    /// <summary>
    /// Execute an operation through the circuit breaker
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="operation">Operation to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the operation</returns>
    public async Task<Result<T>> ExecuteAsync<T>(Func<Task<Result<T>>> operation, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CircuitBreaker));

        lock (_lock)
        {
            switch (_state)
            {
                case CircuitBreakerState.Open:
                    if (DateTime.UtcNow < _nextAttempt)
                    {
                        _logger?.LogDebug("Circuit breaker is open, failing fast");
                        return Result<T>.Failure(new InvalidOperationException("Circuit breaker is open"));
                    }
                    
                    // Transition to half-open to test if service has recovered
                    _state = CircuitBreakerState.HalfOpen;
                    _logger?.LogInformation("Circuit breaker transitioning from Open to HalfOpen");
                    break;

                case CircuitBreakerState.HalfOpen:
                    // Allow the operation to proceed
                    break;

                case CircuitBreakerState.Closed:
                    // Normal operation
                    break;
            }
        }

        try
        {
            var result = await operation();

            if (result.IsSuccessful)
            {
                OnSuccess();
            }
            else
            {
                OnFailure(result.Error);
            }

            return result;
        }
        catch (Exception ex)
        {
            OnFailure(ex);
            return Result<T>.Failure(ex);
        }
    }

    private void OnSuccess()
    {
        lock (_lock)
        {
            _failureCount = 0;
            
            if (_state == CircuitBreakerState.HalfOpen)
            {
                _state = CircuitBreakerState.Closed;
                _logger?.LogInformation("Circuit breaker closed after successful operation");
            }
        }
    }

    private void OnFailure(Exception? exception)
    {
        lock (_lock)
        {
            _failureCount++;
            _logger?.LogWarning(exception, "Circuit breaker failure #{FailureCount}", _failureCount);

            if (_failureCount >= _failureThreshold)
            {
                _state = CircuitBreakerState.Open;
                _nextAttempt = DateTime.UtcNow.Add(_timeout);
                _logger?.LogError("Circuit breaker opened after {FailureCount} failures. Next attempt at {NextAttempt}", 
                    _failureCount, _nextAttempt);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _logger?.LogDebug("Circuit breaker disposed");
    }
}

/// <summary>
/// Retry policy implementation
/// </summary>
public static class RetryPolicy
{
    /// <summary>
    /// Execute operation with exponential backoff retry
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="operation">Operation to execute</param>
    /// <param name="maxAttempts">Maximum retry attempts</param>
    /// <param name="baseDelay">Base delay between retries</param>
    /// <param name="logger">Optional logger</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the operation</returns>
    public static async Task<Result<T>> ExecuteWithRetryAsync<T>(
        Func<Task<Result<T>>> operation,
        int maxAttempts,
        TimeSpan baseDelay,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        Exception? lastException = null;

        while (attempt < maxAttempts)
        {
            try
            {
                var result = await operation();
                
                if (result.IsSuccessful)
                {
                    if (attempt > 0)
                    {
                        logger?.LogInformation("Operation succeeded on attempt {Attempt}", attempt + 1);
                    }
                    return result;
                }

                lastException = result.Error;
                
                if (ShouldRetry(result.Error, attempt, maxAttempts))
                {
                    await DelayForAttempt(attempt, baseDelay, logger, cancellationToken);
                    attempt++;
                    continue;
                }
                
                return result;
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt, maxAttempts))
            {
                lastException = ex;
                await DelayForAttempt(attempt, baseDelay, logger, cancellationToken);
                attempt++;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Operation failed on attempt {Attempt} - not retrying", attempt + 1);
                return Result<T>.Failure(ex);
            }
        }

        logger?.LogError(lastException, "Operation failed after {MaxAttempts} attempts", maxAttempts);
        return Result<T>.Failure(lastException ?? new InvalidOperationException("Operation failed after all retry attempts"));
    }

    private static bool ShouldRetry(Exception? exception, int attempt, int maxAttempts)
    {
        if (attempt >= maxAttempts - 1)
            return false;

        return exception switch
        {
            TaskCanceledException => false, // Don't retry cancelled operations
            ArgumentException => false,     // Don't retry invalid arguments
            HttpRequestException => true,   // Retry network issues
            TimeoutException => true,       // Retry timeouts
            InvalidOperationException => true, // Retry API errors
            _ => true // Retry other exceptions by default
        };
    }

    private static async Task DelayForAttempt(int attempt, TimeSpan baseDelay, ILogger? logger, CancellationToken cancellationToken)
    {
        // Exponential backoff with jitter
        var delay = TimeSpan.FromMilliseconds(
            baseDelay.TotalMilliseconds * Math.Pow(2, attempt) + 
            Random.Shared.Next(0, 1000)); // Add jitter

        logger?.LogDebug("Retrying operation in {Delay}ms (attempt {Attempt})", delay.TotalMilliseconds, attempt + 1);
        
        await Task.Delay(delay, cancellationToken);
    }
}