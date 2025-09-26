# RoofController — Project Overview (v1.3.1)

**Date:** September 26, 2025  
**Owner:** Roy Salisbury  
**System:** Raspberry Pi 5 + Sequent Microsystems SM‑I‑010 + Lenze AC Tech SMVector VFD + ME‑8108 limit switches

---

## 1) Purpose
RoofController automates a motorized roof using a Raspberry Pi 5. The Pi controls a Lenze AC Tech SMVector VFD to drive a SEW‑EURODRIVE motor. The design emphasizes **fail‑safe** operation with normally‑closed (NC) end limits, clear fault handling, and simple, maintainable wiring via DIN distribution blocks.

**Core goals**
- Reliable open/close control with hard end‑stops (NC limit switches).
- Robust fault detection and reset.
- Clear, serviceable wiring (DB‑01 … DB‑10) and printable diagrams.
- A single C# control app driving everything.

---

## 2) Hardware (as built)
- **Compute:** Raspberry Pi 5 (4/8 GB)  
- **I/O HAT:** Sequent Microsystems **SM‑I‑010 Industrial Automation HAT**  
  - 4× relays (RLY1…RLY4)
  - 4× isolated HV inputs (HV‑IN1…HV‑IN4 with per‑channel COM)
- **Drives & Motion:**  
  - **VFD:** Lenze AC Tech **SMVector** (1 HP class)  
  - **Motor:** SEW‑EURODRIVE KA77BDT90S4C (3‑ph)  
  - **End limits:** 2× **ME‑8108** (SPDT 1NO/1NC) roller‑lever switches
- **Panel wiring:** DIN rail, distribution blocks, CAT6 (control), RS‑485 pair (spare), shielded as needed.

**Key terminal nets (per your diagrams)**
- **DB‑01:** +12 V (from VFD **TB‑11**)
- **DB‑02:** 0 V (to VFD **TB‑4**)
- **DB‑03:** FWD limit node (LIMIT1 NC + IN1 monitor + RLY1 COM source)
- **DB‑04:** REV limit node (LIMIT2 NC + IN2 monitor + RLY2 COM source)
- **DB‑05:** RLY1 NO → **TB‑13A (FWD)**
- **DB‑06:** RLY2 NO → **TB‑13B (REV)**
- **DB‑07:** RLY3 NO → **TB‑13C (Clear Fault)**
- **DB‑08:** RLY4 COM → **TB‑1 (STOP)**; RLY4 NC → **DB‑02**
- **DB‑09:** IN4‑COM → **TB‑14 (At‑Speed/Run DO)**; HV‑IN4 → **DB‑01**
- **DB‑10:** HV‑IN3 (IN3+) → **VFD Fault Relay NO**; Fault Relay COM → **DB‑01**
- **Explicit source:** **RLY3 COM → DB‑01 (+12 V)**

> Full wiring/DB diagrams are provided in **Appendix A (A1–A6)** and bundled as **RoofController_Wiring_Diagrams_v1.0_Bundle_2025-09-26.pdf**.

---

## 3) Software
- **Language/Runtime:** C# (.NET 9 / target framework `net9.0` on Raspberry Pi OS 64‑bit)
- **I²C / GPIO:** .NET IoT (`System.Device.Gpio`, `System.Device.I2c`) with SM‑I‑010 abstraction  
  - Methods: `TrySetRelayWithRetry`, `GetAllDigitalInputs`, LED mask updates, and fault clear relay pulse.
- **State machine:** Deterministic with guard conditions from limit inputs, fault input, watchdog timeout, and last command intent. Generates snapshot events (`StatusChanged`).
- **Safety Watchdog:** One‑shot timer restarted on motion. **Code default: 90 seconds.** Current configuration: Development = 120 s, Production = 150 s (see `appsettings*.json`). Timeout triggers emergency stop + status `Error` + stop reason `SafetyWatchdogTimeout`.
- **Optional MODBUS/RS‑485 (future):** Placeholder only; not active in current code.

### Hardware vs Software Layer Distinction
| Aspect | Hardware (Physical / Electrical) | Software (Logical / API) |
|--------|----------------------------------|---------------------------|
| Limit Switch Wiring | ME‑8108 NC continuity until pressed (RAW HIGH = normal, LOW = pressed) | Logical boolean: TRUE = limit reached (polarity independent) |
| Fault Indication | VFD fault relay / output changes electrical level | Logical IN3 TRUE => Status=Error + EmergencyStop semantics |
| Motion Command | Energize RLY1/RLY2 applies run input to drive | Status transitions to Opening/Closing + watchdog start |
| Inhibit / Stop | RLY4 opens enable/stop path when energized | All non-motion states keep RLY4 energized (fail-safe hold) |
| Position Feedback | Only end-stop discrete contacts (no encoder) | Partial vs full inferred from last command + limits |
| Speed Feedback | TB‑14 open-collector sink asserts at speed | IN4 AtSpeed informational (not state driver) |
| Safety Timeout | None inherent (apart from VFD internal) | Software watchdog enforces max motion time |
| Polarity Change | Rewire NC→NO or vice versa | Config flag `UseNormallyClosedLimitSwitches` preserves logical semantics |
| Electrical Failure Mode | Broken NC wire looks like permanent limit reached (safe) | Treated as limit TRUE; motion in that direction inhibited |

RAW references = direct voltage/level; LOGICAL = interpreted boolean after polarity handling.

---

## 4) Normal operation (current implementation)
### Quick Start / First Power-Up Checklist
1. Verify wiring vs diagrams; ensure both limits not mechanically pressed.
2. Power VFD then Pi; wait for service startup.
3. GET `/health` until `Ready:true` and `Status` not `NotInitialized`.
4. Manually actuate each limit (press/release) and GET `/api/v4.0/RoofControl/Status` verifying IN1/IN2 logical toggles.
5. Issue Open: `/api/v4.0/RoofControl/Open`; confirm Status=Opening, watchdog active.
6. Trip open limit (or let travel) → Status=Open, RLY1 de‑energized, RLY4 energized.
7. Issue Close and verify symmetrical behavior.
8. Simulate fault (drive fault output) → Status=Error, LastStopReason=EmergencyStop.
9. POST `/api/v4.0/RoofControl/ClearFault` (pulse default 250 ms) after resolving root cause.
10. Record open & close durations for watchdog tuning (see Watchdog Guidance).
### State model
The controller exposes these operational states:

| State | Meaning |
|-------|---------|
| NotInitialized | Service created but `Initialize` not yet successfully executed. Commands rejected. |
| Unknown | Transitional / indeterminate (should be rare). Health check reports Degraded. |
| Opening | Motion commanded opening and not yet at open limit. |
| Closing | Motion commanded closing and not yet at closed limit. |
| Open | Open limit reached (logical IN1 = TRUE). |
| Closed | Closed limit reached (logical IN2 = TRUE). |
| PartiallyOpen | Motion toward open interrupted before reaching open limit. |
| PartiallyClose | Motion toward close interrupted before reaching closed limit. |
| Stopped | Stopped with neither limit active and no remembered partial context (rare vs partial states). |
| Error | Fault input asserted, contradictory limit condition, safety watchdog timeout, or emergency stop. |

All non‑motion states (everything except Opening/Closing) energize the Stop relay (RLY4) to hold the drive in a safe inhibited condition by design.
### Input polarity (logical view, v1.3.1)
| Input | Logical TRUE (service/API) | Logical FALSE |
|-------|----------------------------|---------------|
| IN1   | Forward/Open limit reached | Not at forward limit |
| IN2   | Reverse/Closed limit reached | Not at reverse limit |
| IN3   | Fault present | No fault |
| IN4   | **AtSpeed** (at commanded speed) | Not at speed |

Physical layer default: **Normally Closed (NC)** limit switches. Raw electrical levels therefore are:
- NC not pressed (travel region) = HIGH
- NC pressed (at limit) = LOW

The service exposes a logical abstraction and includes a configuration option:

`UseNormallyClosedLimitSwitches` (default: true)

When true: RAW LOW → logical TRUE (limit reached).  
When false (Normally Open hardware): RAW HIGH → logical TRUE.

**AppSettings example:**
```
"RoofControllerOptionsV4": {
  "SafetyWatchdogTimeout": "00:02:00",
  "UseNormallyClosedLimitSwitches": true
}
```

**TB‑14 note:** The SMVector **TB‑14** output is an **NPN/open‑collector sink** for “At Speed” (set by **P142=6**). With **HV‑IN4** tied to **DB‑01 (+12 V)** and **IN4‑COM** to **TB‑14**, **IN4 = TRUE** when the drive reaches speed and TB‑14 pulls low. If your unit is PNP/sourcing, swap the IN4 wiring as noted in the diagrams.

### Commands
- **Open:** Refuse if IN3 (fault) TRUE or IN1 (open limit) TRUE. Issue internal stop, energize RLY1, start watchdog, set status `Opening`. On IN1 TRUE → stop & status `Open`.
- **Close:** Refuse if IN3 TRUE or IN2 TRUE. Issue internal stop, energize RLY2, start watchdog, status `Closing`. On IN2 TRUE → stop & status `Closed`.
- **Stop:** Energize RLY4 (Stop relay) + de‑energize motion relays. Status resolves to `PartiallyOpen`, `PartiallyClose`, or `Stopped` based on last intent & limit states.
- **Clear Fault:** Force EmergencyStop, pulse RLY3 (default 250 ms). If IN3 remains TRUE, fault persists.

### Interlocks & behaviors
- **Limits:** Immediate stop + absolute status change when asserted (Stop relay engages).
- **Watchdog:** Triggers emergency stop and sets status `Error` with stop reason `SafetyWatchdogTimeout`.
- **At‑Speed (IN4):** Informational; not used for state transitions (no effect on Opening/Closing computation currently).
- **Fault (IN3):** Any assertion sets status `Error` and records stop intent as Fault/Emergency.

---

## 5) VFD parameter set (summary)
- **P100 = 1** — Start Control Source: Terminal Strip  
- **P101 = 3** — Speed Ref: Preset #1 (use **P131**)  
- **P102 / P103** — Min/Max Hz: *0.0 / 60.0* (or nameplate)  
- **P104 / P105** — Accel/Decel: *≈2.0 s* (tune)  
- **P111 = 2** — Stop Method: Ramp to stop  
- **P112 = 1** — Rotation: Forward + Reverse  
- **P120 = 2** — Assertion Level: **HIGH/+** (and set the **AL switch** to “+”)  
- **P121 / P122 / P123 / P124** — TB‑13A/B/C/D: *13 RunFwd / 14 RunRev / 20 ClearFault / 0 Unused*  
- **P140 = 3** — Relay Output: Fault  
- **P142 = 6** — TB‑14 Output: At Speed  
- **P144 = 0** — Output inversion: None  
- **P131** — Preset #1 speed (e.g., **15.0 Hz**)

*See Appendix **A4** for the VFD single-block terminal map and Appendix **A6** for the full bundle.*

---

## 6) I/O naming (signals)
- **Relays:** RLY1=Open (FWD), RLY2=Close (REV), RLY3=ClearFault (momentary), RLY4=Stop (energize to interrupt TB‑1)
- **Inputs (logical in code):** IN1=OpenLimit (**TRUE when reached**), IN2=ClosedLimit (**TRUE when reached**), IN3=Fault (**TRUE fault**), IN4=**AtSpeed** (**TRUE at commanded speed**)

### LED / Relay / Input Logical Mapping
| Element | Hardware Reference | Logical Representation | Notes |
|---------|--------------------|------------------------|-------|
| RLY1 | RunFwd (TB-13A) | TRUE when energized (Opening) | Drops immediately on limit/stop |
| RLY2 | RunRev (TB-13B) | TRUE when energized (Closing) | Symmetric to RLY1 |
| RLY3 | ClearFault (TB-13C) | Pulsed TRUE during fault clear | Pulse width configurable (default 250 ms) |
| RLY4 | Stop/Enable path | TRUE (energized) in non-motion | Fail-safe hold strategy |
| LED1 | HAT LED bit0 | Mirrors logical Open limit | Set via LED mask update |
| LED2 | HAT LED bit1 | Mirrors logical Closed limit |  |
| LED3 | HAT LED bit2 | Mirrors logical Fault |  |
| IN1 | Forward limit raw | Logical OpenLimit TRUE when limit reached | Polarity applied |
| IN2 | Reverse limit raw | Logical ClosedLimit TRUE when limit reached | Polarity applied |
| IN3 | Fault raw | Logical Fault TRUE when asserted | Drives Error status |
| IN4 | AtSpeed raw | Logical AtSpeed TRUE at commanded speed | Informational only |

### Hardware vs Logical Position Examples
| Scenario | RAW IN1 | RAW IN2 | Logical OpenLimit | Logical ClosedLimit | Software Status (idle) |
|----------|---------|---------|-------------------|---------------------|------------------------|
| Mid travel | HIGH | HIGH | FALSE | FALSE | Stopped / Partial* |
| Fully open | LOW | HIGH | TRUE | FALSE | Open |
| Fully closed | HIGH | LOW | FALSE | TRUE | Closed |
| Dual limit anomaly | LOW | LOW | TRUE | TRUE | Error |
*Partial vs Stopped depends on interruption history.

---

## 7) Truth table — runtime (logical view)
Legend: ON = relay energized / input TRUE; OFF = de‑energized / input FALSE.

| State | RLY1 | RLY2 | RLY3 | RLY4 | IN1 (OpenLimit) | IN2 (ClosedLimit) | IN3 (Fault) | IN4 (**AtSpeed**) |
|-------|------|------|------|------|------------------|-------------------|-------------|-------------------|
| Opening | ON | OFF | OFF | OFF | FALSE | FALSE | FALSE | FALSE→TRUE* |
| Open (limit) | OFF | OFF | OFF | ON | TRUE | FALSE | FALSE | FALSE |
| Closing | OFF | ON | OFF | OFF | FALSE | FALSE | FALSE | FALSE→TRUE* |
| Closed (limit) | OFF | OFF | OFF | ON | FALSE | TRUE | FALSE | FALSE |
| PartiallyOpen | OFF | OFF | OFF | ON | FALSE | FALSE | FALSE | FALSE |
| PartiallyClose | OFF | OFF | OFF | ON | FALSE | FALSE | FALSE | FALSE |
| Stopped | OFF | OFF | OFF | ON | FALSE | FALSE | FALSE | FALSE |
| Error | OFF | OFF | OFF | ON | (prev) | (prev) | TRUE | FALSE |
| Fault clear pulse | OFF | OFF | ON | ON | (prev) | (prev) | TRUE→(FALSE) | FALSE |

*IN4 goes TRUE once the drive reaches the reference speed (not during acceleration). IN4 does not currently influence state transitions.

Inhibits: Refuse **Open** when IN1 TRUE; refuse **Close** when IN2 TRUE. All non‑motion states assert RLY4 (Stop) for fail‑safe hold.

---

## 8) Operator procedures
- **Open roof:** press *Open* → FWD runs until logical Open limit TRUE (RAW LOW when NC) or *Stop* is pressed.  
- **Close roof:** press *Close* → REV runs until logical Closed limit TRUE (RAW LOW when NC) or *Stop* is pressed.  
- **Stop:** press *Stop* → RLY4 energizes; drive ramps to stop.  
- **Clear fault:** investigate cause, ensure safe state, press *Clear* → brief RLY3 pulse. If **IN3** stays HIGH, inspect VFD.

### Power Loss / Recovery Behavior
| Event | Hardware Effect | Software Effect | Operator Guidance |
|-------|-----------------|-----------------|-------------------|
| Pi power loss | All relays de-energize (motion stops) | Service offline | Verify roof stable before restart |
| Pi reboot mid-travel | Motion relays drop, coast to stop | Re-initializes, recalculates from limits | If between limits, expect Partial/Stopped |
| VFD power loss | Run commands ignored, AtSpeed FALSE | Potential Error if fault output asserts | Restore drive power, check faults |
| Limit wiring open (NC) | Appears limit reached (raw LOW unreachable) | Logical limit may appear stuck TRUE | Investigate continuity before forcing motion |
| Fault latch persists | Fault output stays asserted | Status Error remains | Diagnose drive or upstream interlocks |

## 9) REST API Endpoint Summary
All endpoints versioned under `/api/v1.0/roof` (example base path; adjust if deployment path differs). JSON responses use camelCase.

| Method | Route | Description | Success Response | Error Modes |
|--------|-------|-------------|------------------|-------------|
| GET | `/status` | Current status snapshot (idempotent) | 200 + status object | 500 on internal failure |
| POST | `/open` | Initiate open sequence | 202 Accepted + status | 409 if already opening/open; 500 internal |
| POST | `/close` | Initiate close sequence | 202 Accepted + status | 409 if already closing/closed; 500 internal |
| POST | `/stop` | Immediate controlled stop | 200 + status | 409 if already stopped; 500 internal |
| POST | `/clear-fault` | Clear latched drive fault (RLY3 pulse) | 200 + status | 409 if not in Error state; 500 internal |

Status Object (representative):
```
{
  "status": "Opening|Closing|Open|Closed|Partial|Error|Stopped|Unknown|NotInitialized",
  "isReady": true,
  "limits": { "isOpen": false, "isClosed": true },
  "watchdog": { "lastKickUtc": "2025-01-01T00:00:00Z", "isHealthy": true },
  "lastCommandUtc": "2025-01-01T00:00:00Z",
  "stopReason": "None|Commanded|Fault|Watchdog|SafetyInterlock",
  "error": null
}
```

Notes:
- 202 (Accepted) used for motion start where completion is asynchronous.
- 409 (Conflict) used for state-incompatible commands (idempotent safety).
- Fault clearing only allowed when controller is in Error status and fault line indicates latched condition.

## 10) Operator Quick Reference (Cheat Sheet)
| Task | Action | Expected Indicators | If Unexpected |
|------|--------|--------------------|---------------|
| Start Opening | Press Open / POST /open | RLY1 energizes, Open LED flashes, Status Opening | If no relay: check watchdog health & service logs |
| Start Closing | Press Close / POST /close | RLY2 energizes, Close LED flashes, Status Closing | If both relays off: verify limits not both TRUE |
| Stop Motion | Press Stop / POST /stop | Motion relay drops, Status Stopped, Stop reason Commanded | If continues moving: kill motor power, investigate relay weld |
| Clear Fault | Press Clear / POST /clear-fault | RLY3 brief pulse | If pulse absent: confirm Error state and fault line asserted |
| After Power Loss | Wait for service ready | Status transitions NotInitialized → (Closed/Open/Partial) | If stuck NotInitialized: verify GPIO hat detected |
| Verify Limits | Observe limit LEDs | Only one limit TRUE at extremes | If both TRUE: wiring fault or mis-adjusted cams |
| Watchdog Check | Review health endpoint | isHealthy true | If false frequently: tune interval or investigate thread stalls |

Minimal Decision Flow:
1. Is status Error? → Clear Fault (once) → persists? Inspect drive.
2. Is status Partial when at physical limit? → Calibrate limit switch or check polarity config.
3. Are both limits TRUE? → Electrical fault; halt motion until resolved.
4. No motion on command? → Check watchdog health, then relay outputs, then VFD enable power.



---

## 9) Maintenance & tests
- **Limit continuity test:** Meter on **COM & NC** → **beep** when roller **not pressed**; **open** when pressed.  
- **Run output test:** Jog until at speed → **IN4=HIGH**.  
- **Fault relay test:** Trip an interlock or remove enable → **IN3=HIGH**, motion relays must be OFF.

*For continuity testing visuals, see Appendix **A3** (limit switch diagrams with NOTE).* 

---

## 10) Fail‑Safe Philosophy
The control strategy prioritizes a safe, electrically inhibited state whenever motion is not explicitly commanded.

Principles:
- **Energize-to-run:** Motion relays (RLY1/RLY2) are only energized during commanded movement.
- **Fail‑safe hold:** Stop relay (RLY4) remains energized in all non‑motion states to hold the drive’s enable path open (prevents unintended restart after power glitches).
- **Normally Closed limits:** Loss of a limit circuit wire (open) is interpreted as limit reached, preventing further travel in that direction.
- **Single-path intent:** Only one direction relay can be energized; simultaneous Open+Close requests are auto‑sanitized to STOP.
- **Immediate interlock response:** Limits, fault input, watchdog timeout each force an InternalStop (with RLY4 engaged) before status recomputation.
- **No silent overrides:** All abnormal stops produce a distinct `RoofControllerStopReason` for logging & diagnostics.

Operational Implication: Under normal idle conditions additional energy is spent keeping RLY4 energized; this trade‑off is acceptable for positive motion inhibition.

---

## 11) Stop Reasons
| Stop Reason | When Triggered | Typical Root Cause | Operator Action |
|-------------|----------------|--------------------|-----------------|
| None | Internal initialization / neutral events | N/A | None |
| NormalStop | User pressed Stop or commanded halt before limit | Operational pause | Resume if desired |
| LimitSwitchReached | Limit transition detected during motion | Expected travel completion | None (verify position) |
| EmergencyStop | Explicit emergency logic or fault cascade | Manual E‑Stop (future) or fault path | Investigate cause, clear fault |
| StopButtonPressed | (Reserved / future physical input) | Local panel stop | Confirm safe & restart |
| SafetyWatchdogTimeout | Motion exceeded configured watchdog window | Stalled roof, mechanical jam, mis‑set timeout | Inspect mechanism, verify no obstruction |
| SystemDisposal | Service disposal / app shutdown | Application stopping | None (automatic) |

Dual-limit anomaly (both logical limits TRUE) is treated as an error path resulting in `Error` status and `LimitSwitchReached` or later stop semantics; operator should treat as a wiring or sensor fault.

---

## 12) Health & Monitoring
The `/health` endpoint (and tagged checks) expose readiness and operational data:

| Key | Meaning |
|-----|---------|
| IsInitialized | Service successfully initialized |
| IsServiceDisposed | Disposal path has executed (terminal) |
| Status | Current `RoofControllerStatus` enum value |
| LastStopReason | Last `RoofControllerStopReason` enum value |
| IsMoving | Opening or Closing in progress |
| IsWatchdogActive | Watchdog timer currently counting |
| WatchdogSecondsRemaining | Remaining seconds (double) or 0 when inactive |
| Ready | Aggregated: Initialized AND not disposed AND not Error |
| CheckTime | UTC timestamp when sample generated |

Example (healthy Opening):
```json
{
  "IsInitialized": true,
  "IsServiceDisposed": false,
  "Status": "Opening",
  "LastStopReason": "NormalStop",
  "IsMoving": true,
  "IsWatchdogActive": true,
  "WatchdogSecondsRemaining": 81.2,
  "Ready": true,
  "CheckTime": "2025-09-26T18:22:41.103Z"
}
```

Error example (fault asserted):
```json
{
  "IsInitialized": true,
  "IsServiceDisposed": false,
  "Status": "Error",
  "LastStopReason": "EmergencyStop",
  "IsMoving": false,
  "IsWatchdogActive": false,
  "WatchdogSecondsRemaining": 0.0,
  "Ready": false,
  "CheckTime": "2025-09-26T18:29:07.514Z"
}
```

---

## 13) Configuration Summary
| Option | Code Default | Example Dev | Example Prod | Description |
|--------|--------------|-------------|--------------|-------------|
| SafetyWatchdogTimeout | 00:01:30 | 00:02:00 | 00:02:30 | Max continuous motion duration before forced stop |
| UseNormallyClosedLimitSwitches | true | true | true | Interpret RAW LOW as limit reached (NC wiring) |
| EnableDigitalInputPolling | true | true | true | Enables event-driven input edge detection |
| DigitalInputPollInterval | 00:00:00.025 | 00:00:00.025 | 00:00:00.025 | Poll interval for input edge scanning |
| EnablePeriodicVerificationWhileMoving | true | true | true | Periodic direct reads to catch missed edges |
| PeriodicVerificationInterval | 00:00:01 | 00:00:01 | 00:00:01 | Interval between verification reads while moving |
| OpenRelayId | 1 | 1 | 1 | Relay index for Forward/Open command |
| CloseRelayId | 2 | 2 | 2 | Relay index for Reverse/Close command |
| ClearFault | 3 | 3 | 3 | Relay index for fault clear pulse |
| StopRelayId | 4 | 4 | 4 | Relay index for stop (energize-to-hold) |

Note: Changing `UseNormallyClosedLimitSwitches` during active motion may yield transient ambiguous states; prefer applying at startup.

---

## 14) Fault Conditions & Dual-Limit Handling
| Condition | Detection | Result | Recommended Action |
|-----------|-----------|--------|--------------------|
| Fault input asserted (IN3 TRUE) | Raw input event or forced read | Status → Error, Stop engaged | Investigate drive / external interlocks, Clear Fault |
| Safety watchdog timeout | Timer elapsed while Opening/Closing | Status → Error, Stop engaged, reason=SafetyWatchdogTimeout | Inspect for mechanical jam or adjust timeout |
| Both logical limits TRUE | Simultaneous Open+Closed logical evaluation | Status → Error, Stop engaged | Check limit wiring, polarity config, mechanical over-travel |
| Contradictory motion & limit (e.g., Opening but Closed limit TRUE) | State + limit mismatch | Status → Error | Verify relay wiring & limit physical state |

---

## 15) Change Log
| Version | Summary |
|---------|---------|
| v1.3.1 | Added state model, clarified watchdog defaults, enforced RLY4 behavior documentation, truth table corrections, fail‑safe philosophy, stop reasons, health & config sections. |
| v1.3 | AtSpeed naming, wiring clarifications, added explicit RLY3 COM source, diagram bundle zip. |
| v1.2 | Restored NC (LOW=limit) semantics, added `UseNormallyClosedLimitSwitches`. |
| v1.1 | Interim logic (HIGH=limit) alignment, watchdog + truth table refresh. |
| v1.0 | Initial project overview & base diagrams. |

---

## Appendix A — Diagrams & Links

**A1. Distribution Block Diagram (v1.0)**  
[PNG](sandbox:/mnt/data/Distribution_Blocks_All_packed_1to1HalfHeight_v1.0_2025-09-26.png)

**A2. SM‑I‑010 Pin Map (v1.0)**  
[PNG](sandbox:/mnt/data/SM-I-010_Pin_to_DB_Map_v1.0_2025-09-26.png)

**A3. Limit Switches (ME‑8108) with Continuity NOTE (v1.1)**  
[PNG](sandbox:/mnt/data/LIMIT_SWITCHES_ME8108_to_DB_v1.1_with_NOTE.png)

**A4. VFD Single‑Block Terminal Map (v1.2)**  
[PNG](sandbox:/mnt/data/VFD_SingleBlock_DBMap_v1.2.png)

**A5. Wiring Audit Checklist (v1.0)**  
[PNG](sandbox:/mnt/data/Wiring_Audit_Checklist_v1.0.png)

**A6. All Diagrams Bundle (v1.0)**  
[PDF](sandbox:/mnt/data/RoofController_Wiring_Diagrams_v1.0_Bundle_2025-09-26.pdf)
