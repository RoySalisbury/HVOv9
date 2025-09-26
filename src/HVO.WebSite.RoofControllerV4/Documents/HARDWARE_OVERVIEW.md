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

---

## 4) Normal operation (current implementation)
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
