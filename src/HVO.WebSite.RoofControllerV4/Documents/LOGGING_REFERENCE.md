# RoofController Logging Reference (v1.0)

Date: September 26, 2025  
Logging Style: Structured `ILogger<T>` with named placeholders  
Correlation: `traceId` surfaces via ASP.NET Core + can be propagated to client/UI.

---
## 1. Principles
- Consistent templates enable search / aggregation.
- Avoid string concatenation; always use structured placeholders.
- High-frequency events use `Trace`; operational transitions use `Debug` / `Information`.
- Faults / safety trips use `Warning` (recoverable) or `Error` (unexpected / code path exceptions).
- No `Console.WriteLine` or `Debug.WriteLine` in production code.

---
## 2. Log Level Guidelines
| Level | Scope | Examples |
|-------|-------|----------|
| Trace | High-frequency hardware IO, polling, kick heartbeats | Input poll cycle, watchdog kick, LED mask update |
| Debug | State transitions, command acceptance, config load | Status change, command refused, option summary |
| Information | Startup complete, fault cleared, motion cycle summary | Service initialized, watchdog tuned parameters |
| Warning | Safety watchdog timeout, dual-limit anomaly | Watchdog fired, contradictory limit pattern |
| Error | Exception during I/O, unexpected null, disposal error | GPIO access exception, failed relay write |
| Critical | (Rare) unrecoverable hardware layer failure | HAT not detected post-retry |

---
## 3. Event Catalog
### 3.1 Startup
```
Information: RoofControllerService starting - Version={Version}, Options={@Options}
Debug: GPIO Hat detected - Model={Model}, Firmware={Firmware}
Information: RoofControllerService initialized - ElapsedMs={Elapsed}
```

### 3.2 Commands
```
Debug: Command received - Action=Open, Status={CurrentStatus}, IsFaulted={IsFaulted}, OpenLimit={IsOpenLimit}, ClosedLimit={IsClosedLimit}
Debug: Command accepted - Action=Open, WatchdogTimeoutSec={TimeoutSec}
Debug: Command refused - Action=Open, Reason=AlreadyOpen, Status={Status}
```

### 3.3 Motion & Limits
```
Trace: Watchdog kick - RemainingSec={Remaining}, Status={Status}
Debug: Status transition - From={OldStatus}, To={NewStatus}, StopReason={StopReason}
Information: Motion complete - Direction=Open, TravelTimeMs={ElapsedMs}
Warning: Dual limit anomaly - OpenLimit={OpenLimit}, ClosedLimit={ClosedLimit}
```

### 3.4 Faults & Safety
```
Warning: Safety watchdog timeout - Status={StatusBefore}, ElapsedSec={Elapsed}, KickIntervalMs={KickInterval}, MissThreshold={MissThreshold}
Error: Fault asserted - OpenLimit={OpenLimit}, ClosedLimit={ClosedLimit}, LastCommand={LastCommand}
Information: Fault cleared - Attempt={Attempt}, Result={Result}
```

### 3.5 Hardware IO
```
Trace: Relay set - RelayId={RelayId}, TargetState={TargetState}, Attempt={Attempt}, Success={Success}
Trace: Input snapshot - RawMask={RawMask}, OpenLimit={Open}, ClosedLimit={Closed}, Fault={Fault}, AtSpeed={AtSpeed}
Trace: LED mask update - Mask={Mask}, Open={Open}, Closed={Closed}, Fault={Fault}
```

### 3.6 Disposal
```
Information: RoofControllerService disposing - Status={Status}, IsMoving={IsMoving}
Debug: Watchdog cancelled - Reason=Disposal
Information: RoofControllerService disposed - ElapsedMs={Elapsed}
```

---
## 4. Correlation & Trace IDs
- Each HTTP request includes `traceId` (Activity ID) accessible in logs.
- For background operations (watchdog, timers), create or continue ambient `Activity` where beneficial.
- Include `CorrelationId={CorrelationId}` in multi-step operations if user-initiated sequences span multiple requests.

---
## 5. Structured Data Conventions
| Placeholder | Type | Notes |
|-------------|------|-------|
| Version | string | Assembly or semantic version |
| Options | object | Serialized options snapshot (ensure safe fields only) |
| Status | string | Current state enum |
| OldStatus / NewStatus | string | Transition pair |
| StopReason | string | Reason associated with transition |
| OpenLimit / ClosedLimit | bool | Logical limit interpretations |
| Fault | bool | Fault logical state |
| Remaining / Elapsed / ElapsedMs | number | Time intervals; prefer ms for precision |
| KickInterval | number | Kick interval ms (future enhancement) |
| MissThreshold | int | Miss threshold count (future enhancement) |
| RelayId | int | Hardware relay index (1..4) |
| TargetState / Success | bool | Operation intent vs result |
| RawMask / Mask | int/hex | Raw bit masks (format as hex) |
| LastCommand | string | Last command direction or None |
| Attempt | int | Retry attempt counter |
| Result | string | Result classification or enum |

---
## 6. Example Correlated Sequence (Open → Fault)
1. Command accepted
2. Status transitions Opening
3. Input snapshot cycles (Trace)
4. Fault asserted (Error) — Warning/Error pair
5. Fault clear attempt
6. Status transitions (Error → Opening) if fault clears and Open re-issued

Search strategy (structured log store):
```
Action=Open AND StatusTransition AND NewStatus=Opening
```
Then follow subsequent `traceId`.

---
## 7. Log Volume Management
| Strategy | Rationale |
|----------|-----------|
| Use Trace only for development or deep diagnostics | Reduce production noise |
| Batch LED / input snapshots (coalesce) | Avoid redundant high-frequency entries |
| Suppress unchanged status transitions | Prevent log inflation |
| Convert watchdog to multi-line summary every N kicks | Balance insight vs noise |

---
## 8. Change Log
| Version | Notes |
|---------|-------|
| 1.0 | Initial extraction & normalization of patterns.

---
## 9. References
- `HARDWARE_OVERVIEW.md`
- `API_REFERENCE.md`
