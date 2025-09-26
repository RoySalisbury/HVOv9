# RoofController Troubleshooting Guide (v1.0)

Date: September 26, 2025  
Scope: Rapid diagnostic mapping between observed symptoms, /health fields, and corrective actions.

---
## 1. How to Use
1. Query runtime status: `GET /api/v1.0/roof/status` (or legacy `/api/v4.0/RoofControl/Status` during transition).
2. Query health snapshot: `GET /health` (look for the RoofController entry data block).
3. Match fields & symptoms below; apply corrective action.

---
## 2. Key /health Fields
| Field | Meaning | Normal When |
|-------|---------|-------------|
| IsInitialized | Hardware + service init complete | true after startup grace period |
| IsServiceDisposed | Service disposed/shut down | false during operation |
| Status | Primary state enum | Not Error / Unknown / NotInitialized during steady-state |
| LastStopReason | Reason for previous stop | LimitSwitchReached or NormalStop commonly |
| IsMoving | Motion in progress | true only during Opening/Closing |
| IsWatchdogActive | Watchdog timing motion | true only while moving |
| WatchdogSecondsRemaining | Remaining motion allowance | >0 and decreasing while moving |
| Ready | Aggregated readiness flag | true when safe for commands |

---
## 3. Symptom → Diagnosis
| Symptom | /health Clues | Root Cause Candidates | Corrective Action |
|---------|---------------|-----------------------|------------------|
| Cannot issue Open (ignored) | Status=Open OR limits.isOpen=true | Already open | None (idempotent) |
| Cannot issue Close (ignored) | Status=Closed OR limits.isClosed=true | Already closed | None (idempotent) |
| Immediate Error after command | Status=Error, LastStopReason=EmergencyStop, Fault line asserted | Drive fault, external interlock | Inspect drive panel; clear fault; POST /clear-fault |
| Dual-limit anomaly | Status=Error, both limits true (if exposed) | Wiring short, mis-adjusted switches | Inspect limit wiring and mechanical cams |
| Watchdog timeout | LastStopReason=SafetyWatchdogTimeout, IsWatchdogActive=false | Mechanical jam, mis-tuned timeout | Inspect mechanism; adjust `SafetyWatchdogTimeout` |
| Stuck NotInitialized | IsInitialized=false | Hardware HAT not detected, init exception | Review logs; verify I2C / power; restart service |
| Frequent partial stops | LastStopReason=NormalStop repeatedly mid-travel | Operator stops, marginal power, vibration hits limit | Confirm limits, reduce nuisance stops, inspect drive |
| Fault persists after clear | Status=Error after /clear-fault | True drive fault (active) | Retrieve drive fault code; clear at drive then retry |
| AtSpeed/Run never true | telemetry.atSpeedRun=false while moving | P142 mis-config, wiring TB-14, low speed below threshold | Verify VFD P142=6, wiring to IN4, speed setpoint |
| Status flickers Unknown | Transient initialization or race | Rare snapshot overlap | If persistent: enable Trace logs and capture sequence |
| Both limits false but physically at limit | Limit wiring open / wrong polarity | Wrong polarity setting | Check `UseNormallyClosedLimitSwitches` vs hardware |
| Ready=false while not Error | IsServiceDisposed=true OR IsInitialized=false | Shutdown or still starting | Wait or restart service |

---
## 4. Procedural Checks
### 4.1 Limit Switch Verification
1. Roof mid-travel: both limits logical FALSE.
2. Manually engage Open limit → limits.isOpen TRUE only.
3. Release; engage Closed limit → limits.isClosed TRUE only.
4. If both TRUE or both FALSE at end positions: adjust / repair.

### 4.2 Fault Recovery
1. Identify underlying drive/interlock fault (panel indicators).
2. Resolve cause (e.g., overload, overcurrent, undervoltage).
3. POST `/api/v1.0/roof/clear-fault`.
4. Confirm Status!=Error and proceed.

### 4.3 Watchdog Tuning Snapshot
While moving:
- `IsWatchdogActive` = true
- `WatchdogSecondsRemaining` decreases steadily
After timeout event:
- Status=Error
- LastStopReason=SafetyWatchdogTimeout

---
## 5. Decision Flow (Compact)
```
Error? -> Fault asserted? -> Clear Fault -> persists? Inspect drive.
Dual limit? -> Stop operations -> Inspect wiring.
NotInitialized beyond startup window? -> Check logs & HAT detection.
Watchdog timeout? -> Mechanism & tuning review.
Partial stops increasing? -> Evaluate operator intent vs mechanical drag.
```

---
## 6. Data to Collect Before Escalation
| Category | Items |
|----------|-------|
| Status | /status payload, /health snapshot |
| Logs | Last 200 lines (Info+ Warning/Error) around event |
| Environment | Temperature, humidity (if weather integrated) |
| Hardware | Limit physical state, relay LED states, drive fault code |
| Config | Current `SafetyWatchdogTimeout`, polarity flag |

---
## 7. Future Enhancements
| Idea | Benefit |
|------|---------|
| Include limits + fault booleans directly in /health data block | Faster operator diagnosis |
| Add rolling watchdog jitter metrics | Performance tuning clarity |
| Provide /diagnostics endpoint with combined snapshot | Single-call triage |

---
## 8. Change Log
| Version | Notes |
|---------|-------|
| 1.0 | Initial troubleshooting mapping.

---
References: `HARDWARE_OVERVIEW.md`, `API_REFERENCE.md`, `OPERATOR_CHEAT_SHEET.md`, `LOGGING_REFERENCE.md`
