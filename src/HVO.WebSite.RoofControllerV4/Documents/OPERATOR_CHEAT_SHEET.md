# RoofController Operator Cheat Sheet (v1.0)

Date: September 26, 2025  
Intended Use: Printed quick reference for field operators

---
## 1. Core States
| State | Meaning | Operator Action |
|-------|---------|-----------------|
| Open | Roof fully open (Open limit) | None |
| Closed | Roof fully closed (Closed limit) | None |
| Opening / Closing | Motion in progress | Monitor; ensure path clear |
| PartiallyOpen / PartiallyClose | Stopped mid-travel in respective direction | Resume or Close/Open |
| Stopped | Idle mid-travel (rare) | Decide desired direction |
| Error | Fault/timeout/dual-limit | Diagnose + Clear Fault |
| NotInitialized | Service not ready | Wait / check service logs |

---
## 2. Buttons / API
| Action | Local Button | API | Effect |
|--------|--------------|-----|-------|
| Open | Open | POST /open | Energize FWD relay until open limit |
| Close | Close | POST /close | Energize REV relay until closed limit |
| Stop | Stop | POST /stop | Drop motion relay, hold inhibited |
| Clear Fault | Clear Fault | POST /clear-fault | Pulse fault clear relay |
| Status | (N/A) | GET /status | JSON snapshot |

---
## 3. Indicators (Logical)
| Indicator | TRUE Means | If Unexpected |
|-----------|------------|---------------|
| OpenLimit | At open end limit | If stuck TRUE mid-travel → wiring fault |
| ClosedLimit | At closed end limit | If stuck TRUE mid-travel → wiring fault |
| Fault | Drive or interlock fault present | Inspect drive panel + logs |
| AtSpeed | Drive at commanded speed | May be FALSE during accel/decel |

---
## 4. Normal Motion Cycle
1. Issue Open/Close.
2. Motion relay energizes (RLY1 or RLY2).
3. AtSpeed may assert (informational).
4. Limit reached → motion relay drops, Stop relay engaged.
5. Status becomes Open/Closed.

---
## 5. Fault / Error Recovery
| Symptom | Likely Cause | Steps |
|---------|--------------|-------|
| Status=Error immediately on motion | Fault input active | Inspect drive, clear external fault |
| Both limits TRUE | Wiring short or mis-adjusted cams | Physically inspect both switches |
| Watchdog timeout | Mechanical jam or mis-tuned timeout | Inspect roof path, verify limit operation |
| Cannot Clear Fault (remains Error) | Persistent drive fault | Check drive display / diagnostics |

Procedure:
1. Ensure path safe (no obstruction).
2. Clear root cause (drive / wiring / obstruction).
3. Press Clear Fault (or POST /clear-fault).
4. Re-issue motion command.

---
## 6. Quick Decision Flow
```
Error?
  Yes -> Clear Fault -> still Error? Inspect drive/wiring.
Partial but physically at limit?
  -> Adjust limit / polarity, then retry.
Both limits TRUE?
  -> Stop. Diagnose wiring.
No motion on Open/Close?
  -> Check Fault, then Status, then relays.
```

---
## 7. Power / Recovery
| Event | Expected Result | Operator Note |
|-------|-----------------|---------------|
| Pi reboot mid-travel | Motion stops, recalculates to Partial | Resume desired direction |
| Drive power loss | Fault or no motion | Restore power, check fault LED |
| Limit wiring break (NC) | Appears limit reached (safe) | Repair before forcing motion |

---
## 8. Safety Philosophy (Condensed)
- Fail-safe: loss of control power removes motion relay drive.
- Stop relay energized in all non-motion states to inhibit unintended restart.
- NC limits default ensure open circuit = safe stop.

---
## 9. Contact / Escalation
| Level | When | Action |
|-------|------|--------|
| Tier 1 | Basic motion or simple fault | Follow sections 5–6 |
| Tier 2 | Repeated watchdog timeouts | Inspect mechanics & log bundle |
| Tier 3 | Dual-limit or unrecoverable faults | Electrical inspection + engineering review |

---
## 10. Change Log
| Version | Notes |
|---------|-------|
| 1.0 | Initial operator extraction from full overview.

---
References: `HARDWARE_OVERVIEW.md`, `API_REFERENCE.md`, `LOGGING_REFERENCE.md`
