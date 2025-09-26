# RoofController — Project Overview (v1.2)

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

> Full wiring/DB diagrams are provided in **Appendix A (A1–A6)** and bundled as **RoofController_Wiring_Diagrams_v1.0_Bundle_2025-09-26.pdf**.

---

## 3) Software
- **Language/Runtime:** C# (.NET 9 / target framework `net9.0` on Raspberry Pi OS 64‑bit)
- **I²C / GPIO:** .NET IoT (`System.Device.Gpio`, `System.Device.I2c`) with SM‑I‑010 abstraction
  - Methods: `TrySetRelayWithRetry`, `GetAllDigitalInputs`, LED mask updates, and fault clear relay pulse.
- **State machine:** Deterministic with guard conditions from limit inputs, fault input, watchdog timeout, and last command intent. Generates snapshot events (`StatusChanged`).
- **Safety Watchdog:** One‑shot timer (default 90 s) restarted on motion. Timeout triggers emergency stop + status `Error` + stop reason `SafetyWatchdogTimeout`.
- **Optional MODBUS/RS‑485 (future):** Placeholder only; not active in current code.

---

## 4) Normal operation (current implementation)
### Input polarity (logical view, v1.2)
| Input | Logical TRUE (service/API) | Logical FALSE |
|-------|---------------------------|---------------|
| IN1   | Forward/Open limit reached | Not at forward limit |
| IN2   | Reverse/Closed limit reached | Not at reverse limit |
| IN3   | Fault present | No fault |
| IN4   | Movement/At‑speed advisory | Inactive |

Physical layer default: **Normally Closed (NC)** limit switches. Raw electrical levels therefore are:
- NC not pressed (travel region) = HIGH
- NC pressed (at limit) = LOW

The service now exposes a logical abstraction and includes a configuration option:

`UseNormallyClosedLimitSwitches` (default: true)

When true: RAW LOW → logical TRUE (limit reached).  
When false (Normally Open hardware): RAW HIGH → logical TRUE.

AppSettings example:
```
"RoofControllerOptionsV4": {
  "SafetyWatchdogTimeout": "00:02:00",
  "UseNormallyClosedLimitSwitches": true
}
```

### Commands
- **Open:** Refuse if IN3 (fault) TRUE or IN1 (open limit) TRUE. Issue internal stop, energize RLY1, start watchdog, set status `Opening`. On IN1 TRUE → stop & status `Open`.
- **Close:** Refuse if IN3 TRUE or IN2 TRUE. Issue internal stop, energize RLY2, start watchdog, status `Closing`. On IN2 TRUE → stop & status `Closed`.
- **Stop:** Energize RLY4 (Stop relay) + de‑energize motion relays. Status resolves to `PartiallyOpen`, `PartiallyClose`, or `Stopped` based on last intent & limit states.
- **Clear Fault:** Force EmergencyStop, pulse RLY3 (default 250 ms). If IN3 remains TRUE, fault persists.

### Interlocks & behaviors
- **Limits:** Immediate stop + absolute status change when asserted.
- **Watchdog:** Triggers emergency stop and sets status `Error` with stop reason `SafetyWatchdogTimeout`.
- **Movement advisory (IN4):** Not yet used to transition states; available for future speed/telemetry augmentation.
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
- **Relays:** RLY1=Open(FWD), RLY2=Close(REV), RLY3=ClearFault (momentary), RLY4=Stop (energize to interrupt TB‑1)
- **Inputs (logical in code):** IN1=OpenLimit (TRUE when reached), IN2=ClosedLimit (TRUE when reached), IN3=Fault (TRUE fault), IN4=Movement advisory.

---

## 7) Truth table — runtime (logical view)
Legend: ON = relay energized / input TRUE; OFF = de‑energized / input FALSE.

| State | RLY1 | RLY2 | RLY3 | RLY4 | IN1 (OpenLimit) | IN2 (ClosedLimit) | IN3 (Fault) | IN4 (Advisory) |
|-------|------|------|------|------|-----------------|-------------------|------------|---------------|
| Idle | OFF | OFF | OFF | OFF | FALSE | FALSE | FALSE | FALSE |
| Opening | ON | OFF | OFF | OFF | FALSE | FALSE | FALSE | FALSE→TRUE* |
| Open (limit) | OFF | OFF | OFF | OFF | TRUE | FALSE | FALSE | FALSE |
| Closing | OFF | ON | OFF | OFF | FALSE | FALSE | FALSE | FALSE→TRUE* |
| Closed (limit) | OFF | OFF | OFF | OFF | FALSE | TRUE | FALSE | FALSE |
| Stopped (manual) | OFF | OFF | OFF | ON | (prev) | (prev) | FALSE | FALSE |
| Fault | OFF | OFF | OFF | ON | (prev) | (prev) | TRUE | FALSE |
| Fault clear pulse | OFF | OFF | ON | ON | (prev) | (prev) | TRUE→(FALSE) | FALSE |

*IN4 currently informational only.

Inhibits: Refuse Open when IN1 TRUE; refuse Close when IN2 TRUE.

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

## 10) Change log
- **v1.2** — Restored NC (LOW=limit) semantics in code, added config `UseNormallyClosedLimitSwitches`, updated polarity tables & operator procedures.
- **v1.1** — Interim code alignment (input polarity HIGH=limit, watchdog, .NET 9, updated truth table, clarified RLY de‑energize on limit, advisory IN4).
- **v1.0** — Initial project overview; matches wiring bundle v1.0 diagrams.



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

