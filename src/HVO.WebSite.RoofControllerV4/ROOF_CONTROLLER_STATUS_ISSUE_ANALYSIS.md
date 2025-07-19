# RoofController Status Transition Issue - Root Cause Analysis and Fix

## Problem Description
The Blazor UI shows incorrect roof status and button states because:
1. **Opening** never transitions to **Open**
2. **Closing** never transitions to **Closed**  
3. Both real and mock controllers have status transition bugs

## Root Cause Analysis

### Issue #1: MockRoofController Never Transitions Final States
**Problem**: The `MockRoofController` has no mechanism to simulate completion of operations.

```csharp
// Current MockRoofController behavior:
Open() → Status = Opening (stays Opening forever) ❌
Close() → Status = Closing (stays Closing forever) ❌
```

**Expected behavior**:
```csharp
Open() → Status = Opening → (after delay) → Status = Open ✅
Close() → Status = Closing → (after delay) → Status = Closed ✅
```

### Issue #2: RoofController InternalStop Logic Bug
**Problem**: When limit switches trigger, `InternalStop()` sets status to `Stopped` but `UpdateRoofStatus()` doesn't properly set final states.

**Current logic in limit switch handlers**:
```csharp
private void roofOpenLimitSwitch_LimitSwitchTriggered(...)
{
    // When limit switch contacted:
    this.InternalStop();  // Sets Status = Stopped ❌
    this.UpdateRoofStatus(); // Should set Status = Open, but doesn't work correctly
}
```

**Current `InternalStop()` logic**:
```csharp
private void InternalStop()
{
    this.Status = RoofControllerStatus.Stopped; // ❌ Always sets to Stopped first
    // ... GPIO operations ...
    this.UpdateRoofStatus(); // ❌ Doesn't override the Stopped status correctly
}
```

### Issue #3: UpdateRoofStatus Logic Inconsistency
**Problem**: The `UpdateRoofStatus()` method has logic that doesn't properly handle the limit switch triggered states.

## Required Fixes

### Fix #1: Enhance MockRoofController with Timer-Based Transitions
Add background timers to simulate realistic roof operation timing:

```csharp
public class MockRoofController : IRoofController
{
    private System.Timers.Timer? _operationTimer;
    private readonly TimeSpan _simulatedOperationTime = TimeSpan.FromSeconds(10);

    public Result<RoofControllerStatus> Open()
    {
        _logger.LogInformation("MockRoofController: Open called");
        Status = RoofControllerStatus.Opening;
        StartOperationSimulation(RoofControllerStatus.Open);
        return Result<RoofControllerStatus>.Success(Status);
    }

    private void StartOperationSimulation(RoofControllerStatus targetStatus)
    {
        _operationTimer?.Stop();
        _operationTimer?.Dispose();
        
        _operationTimer = new System.Timers.Timer(_simulatedOperationTime.TotalMilliseconds);
        _operationTimer.Elapsed += (sender, e) => CompleteOperation(targetStatus);
        _operationTimer.AutoReset = false;
        _operationTimer.Start();
    }

    private void CompleteOperation(RoofControllerStatus targetStatus)
    {
        Status = targetStatus;
        _logger.LogInformation("MockRoofController: Operation completed - Status = {Status}", Status);
    }
}
```

### Fix #2: Fix RoofController InternalStop Logic
Modify `InternalStop()` to not always set status to `Stopped`:

```csharp
private void InternalStop(bool shouldUpdateStatus = true)
{
    // Don't automatically set to Stopped - let UpdateRoofStatus determine final state
    if (shouldUpdateStatus)
    {
        this.UpdateRoofStatus(); // Determine actual status based on limit switches
    }
    
    // Set relays to safe state...
    SetRelayStatesAtomically(...);
    
    _logger.LogInformation($"====InternalStop - Final Status: {this.Status}");
}
```

### Fix #3: Enhance UpdateRoofStatus Logic
Improve the status determination logic to handle transitional states properly:

```csharp
private void UpdateRoofStatus()
{
    lock (this._syncLock)
    {
        var openTriggered = this._roofOpenLimitSwitch?.IsTriggered ?? false;
        var closedTriggered = this._roofClosedLimitSwitch?.IsTriggered ?? false;

        if (openTriggered && !closedTriggered)
        {
            this.Status = RoofControllerStatus.Open;
            _logger.LogInformation("Roof position: OPEN (open limit switch triggered)");
        }
        else if (!openTriggered && closedTriggered)
        {
            this.Status = RoofControllerStatus.Closed;
            _logger.LogInformation("Roof position: CLOSED (closed limit switch triggered)");
        }
        else if (openTriggered && closedTriggered)
        {
            // Error state - both switches triggered
            this.Status = RoofControllerStatus.Error;
            this._logger.LogError("Hardware error: Both limit switches triggered simultaneously");
        }
        else
        {
            // Neither switch triggered - roof is between positions
            // Only override current status if we're not in a movement state
            if (Status != RoofControllerStatus.Opening && Status != RoofControllerStatus.Closing)
            {
                this.Status = RoofControllerStatus.Stopped;
            }
            _logger.LogInformation("Roof position: BETWEEN (no limit switches triggered, status: {Status})", Status);
        }
    }
}
```

## Implementation Priority
1. **Fix MockRoofController** (immediate impact for UI testing)
2. **Fix RoofController InternalStop** (hardware behavior)
3. **Enhance UpdateRoofStatus** (comprehensive status logic)

## Testing Strategy
1. Test MockRoofController transitions with timers
2. Verify button enable/disable states reflect final positions
3. Test limit switch simulation in Blazor component
4. Verify hardware controller behavior with mocked GPIO
