# HVO.NinaClient Resilience Architecture

## Overview

The HVO.NinaClient implements a comprehensive resilience pattern to handle failures when communicating with the NINA astronomy software API. The resilience architecture uses two complementary patterns:

1. **Retry Policy** - Handles transient failures with exponential backoff
2. **Circuit Breaker** - Prevents cascading failures by temporarily stopping calls to failing services

## Architecture Flow

```
API Call Request
      ↓
ExecuteWithResilienceAsync()
      ↓
[LAYER 1] RetryPolicy.ExecuteWithRetryAsync()
      ↓
Actual API Operation (GetAsync<T>)
      ↓
[LAYER 2] CircuitBreaker.ExecuteAsync() (if enabled)
      ↓
Result<T> Response
```

## Layer 1: Retry Policy

### Purpose
Handles transient failures like network timeouts, temporary API unavailability, or brief service disruptions.

### Key Features
- **Exponential Backoff**: Delay doubles with each retry (1s, 2s, 4s, 8s...)
- **Jitter**: Adds random 0-1000ms to prevent thundering herd
- **Smart Retry Logic**: Only retries appropriate exception types
- **Configurable**: Max attempts and base delay from `NinaApiClientOptions`

### Retry Strategy
```csharp
// Retryable Exceptions (potentially transient)
HttpRequestException     // Network connectivity issues
TimeoutException        // Request timeouts
InvalidOperationException // API logical errors

// Non-Retryable Exceptions (permanent failures)
TaskCanceledException   // User cancellation
ArgumentException      // Invalid parameters
```

### Example Retry Sequence
```
Attempt 1: Fails → Wait ~1s → Retry
Attempt 2: Fails → Wait ~2s → Retry  
Attempt 3: Fails → Wait ~4s → Retry
Attempt 4: Success ✓
```

## Layer 2: Circuit Breaker

### Purpose
Prevents cascading failures by "opening" the circuit when too many failures occur, allowing the failing service time to recover.

### State Machine

#### CLOSED (Normal Operation)
- All API calls proceed normally
- Failure count tracked
- Transitions to OPEN when failure threshold reached

#### OPEN (Failing Fast) 
- All API calls fail immediately without execution
- Saves resources and prevents cascading failures
- Transitions to HALF-OPEN after timeout period

#### HALF-OPEN (Testing Recovery)
- Allows one API call to test if service recovered
- Success → Transitions back to CLOSED
- Failure → Transitions back to OPEN

### Configuration
```csharp
// From NinaApiClientOptions
EnableCircuitBreaker = true
CircuitBreakerFailureThreshold = 5        // Failures before opening
CircuitBreakerTimeoutSeconds = 30         // How long to stay open
```

### Circuit Breaker Flow
```
Normal Operation (CLOSED)
    ↓ (5 failures)
Circuit Opens (OPEN) - Fail Fast
    ↓ (30 seconds later)  
Testing Recovery (HALF-OPEN)
    ↓ Success → CLOSED
    ↓ Failure → OPEN (repeat)
```

## Configuration Options

### Retry Configuration
```json
{
  "NinaApiClient": {
    "MaxRetryAttempts": 3,          // Max retry attempts
    "RetryDelayMs": 1000            // Base delay between retries
  }
}
```

### Circuit Breaker Configuration
```json
{
  "NinaApiClient": {
    "EnableCircuitBreaker": true,
    "CircuitBreakerFailureThreshold": 5,    // Failures before opening
    "CircuitBreakerTimeoutSeconds": 30      // Seconds to stay open
  }
}
```

## Benefits

### Retry Policy Benefits
- **Handles Transient Failures**: Automatically recovers from temporary issues
- **Exponential Backoff**: Reduces load on struggling services
- **Jitter**: Prevents synchronized retry storms
- **Selective Retry**: Only retries appropriate error types

### Circuit Breaker Benefits  
- **Prevents Cascading Failures**: Stops calling failing services
- **Resource Conservation**: Avoids wasting time/resources on doomed calls
- **Fast Recovery**: Quickly detects when service recovers
- **System Stability**: Maintains overall system health

## Usage Example

```csharp
// All NinaApiClient methods automatically use resilience
var ninaClient = new NinaApiClient(httpClient, logger, options);

// This call automatically includes:
// 1. Up to 3 retry attempts with exponential backoff
// 2. Circuit breaker protection (if enabled)
var result = await ninaClient.GetCameraInfoAsync(cancellationToken);

if (result.IsSuccessful)
{
    var cameraInfo = result.Value;
    // Use camera information
}
else
{
    // Handle failure - could be from actual API error,
    // retry exhaustion, or circuit breaker open
    logger.LogError(result.Error, "Failed to get camera info");
}
```

## Monitoring and Diagnostics

### Logging
- Retry attempts logged at Debug level
- Circuit breaker state changes logged at Information/Error level
- Failure patterns logged at Warning level

### Diagnostics
```csharp
var diagnostics = ninaClient.GetDiagnostics();
Console.WriteLine($"Circuit Breaker State: {diagnostics.CircuitBreakerState}");
Console.WriteLine($"Failure Count: {diagnostics.CircuitBreakerFailureCount}");
```

## Best Practices

1. **Configure Appropriately**: Set retry and circuit breaker thresholds based on your service's characteristics
2. **Monitor Patterns**: Watch logs for repeated failures indicating systemic issues
3. **Handle Results**: Always check `Result<T>.IsSuccessful` before using values
4. **Consider Context**: Some operations (like emergency stops) may need different resilience settings
5. **Test Scenarios**: Verify behavior under various failure conditions

## Real-World Scenarios

### Scenario 1: Temporary Network Glitch
- Retry Policy handles it with 1-2 retry attempts
- Circuit Breaker remains CLOSED
- Operation succeeds transparently

### Scenario 2: NINA Service Restart
- First few calls fail → Retry Policy attempts
- After threshold failures → Circuit Breaker OPENS
- Service recovers → Circuit Breaker detects recovery via HALF-OPEN test
- Normal operation resumes

### Scenario 3: Persistent Configuration Error  
- Retry Policy attempts but fails (non-retryable ArgumentException)
- Fails fast without exhausting retries
- Circuit Breaker may open if pattern continues
- Requires manual intervention to fix root cause

This resilience architecture ensures the HVO.NinaClient is robust and reliable when communicating with the NINA astronomy software, gracefully handling both temporary glitches and persistent failures.
