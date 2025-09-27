# RoofController REST API Reference (v4.0)

Date: September 27, 2025  
Scope: Roof motion control / status / fault management  
Base Path (default deployment): `/api/v4.0/RoofControl`

> Host name / path base will depend on the environment (reverse proxy, container, etc.). All responses are JSON (`application/json; charset=utf-8`).

---
## 1. Resource Overview
| Endpoint | Method | Summary | Idempotent | Typical Success | Notes |
|----------|--------|---------|------------|-----------------|-------|
| `/Status` | GET | Current controller snapshot | YES | 200 (RoofStatusResponse) | Safe for UI refresh / polling |
| `/Open` | GET | Begin open motion | YES<sup>*</sup> | 200 (RoofStatusResponse) | Returns immediately; no-op if already Open/Opening |
| `/Close` | GET | Begin close motion | YES<sup>*</sup> | 200 (RoofStatusResponse) | Returns immediately; no-op if already Closed/Closing |
| `/Stop` | GET | Graceful stop & inhibit | YES | 200 (RoofStatusResponse) | Drops motion relays and recomputes status |
| `/ClearFault` | POST | Pulse Clear-Fault relay | NO | 200 (bool) | Body is `true` on success; requires `Status=Error` |

<sup>*</sup>Open/Close ignore duplicate requests for the same direction while motion is already in progress or at the corresponding limit, so the operations behave idempotently from the client perspective.

---
## 2. Status Model (`RoofStatusResponse`)
Representative payload (fields are additive forward-compatible):
```json
{
  "status": "Opening",
  "isMoving": true,
  "lastStopReason": "NormalStop",
  "lastTransitionUtc": "2025-09-26T19:05:31.044Z",
  "isWatchdogActive": true,
  "watchdogSecondsRemaining": 83.4,
  "isAtSpeed": false
}
```

### 2.1 Enumerations
`status` one of:
```
NotInitialized | Unknown | Opening | Closing | Open | Closed | PartiallyOpen | PartiallyClose | Stopped | Error
```
`lastStopReason` one of (may be `None` while moving):
```
None | NormalStop | LimitSwitchReached | EmergencyStop | StopButtonPressed | SafetyWatchdogTimeout | SystemDisposal
```

### 2.2 Field Notes
| Field | Type | Description |
|-------|------|-------------|
| status | string | Current state machine status |
| isMoving | bool | `true` when status is Opening or Closing |
| lastStopReason | string | Reason recorded when the controller last transitioned out of motion |
| lastTransitionUtc | string (UTC ISO8601) | Timestamp of the most recent state transition |
| isWatchdogActive | bool | `true` while the motion watchdog timer is running |
| watchdogSecondsRemaining | number\|null | Seconds remaining before watchdog would trigger; `null` when inactive |
| isAtSpeed | bool | `true` when the VFD indicates the drive is at commanded speed (TB-14 → IN4) |

---
## 3. Endpoint Details

### 3.1 GET `/Status`
Returns the latest `RoofStatusResponse` snapshot. The service performs a lightweight refresh prior to returning the snapshot to include the latest AtSpeed telemetry.

**Success (200)**  
Body = `RoofStatusResponse`

**Failure**  
500 — Internal error / service not initialized (ProblemDetails)

---
### 3.2 GET `/Open`
Initiates opening motion when safe. The controller enforces STOP-first sequencing and hardware interlocks.

| Code | Meaning |
|------|---------|
| 200 | Command accepted or already opening/open; body contains updated `RoofStatusResponse` |
| 500 | Service error (ProblemDetails) |

---
### 3.3 GET `/Close`
Mirrors `/Open` for the closing direction.

| Code | Meaning |
|------|---------|
| 200 | Command accepted or already closing/closed |
| 500 | Service error (ProblemDetails) |

---
### 3.4 GET `/Stop`
Requests an immediate controlled stop (releases motion relays, maintains STOP inhibit).

| Code | Meaning |
|------|---------|
| 200 | Stop processed (idempotent) |
| 500 | Service error (ProblemDetails) |

---
### 3.5 POST `/ClearFault`
Pulses the clear-fault relay (default 250 ms) after issuing an emergency stop.

| Code | Meaning |
|------|---------|
| 200 | Pulse completed; body is `true`/`false` indicating whether the pulse executed |
| 500 | Service error (ProblemDetails) |

**Query Parameters**

| Name | Type | Default | Description |
|------|------|---------|-------------|
| `pulseMs` | int | 250 | Duration (milliseconds) to hold the Clear-Fault relay active |

---
### 3.6 GET `/health`
Standard ASP.NET Core health endpoint. The RoofController registration is tagged with `"roof"` and `"hardware"`.

Excerpt:
```json
{
  "status": "Healthy",
  "checks": [
    {
      "name": "roof_controller",
      "status": "Healthy",
      "data": {
        "IsInitialized": true,
        "IsServiceDisposed": false,
        "Status": "Opening",
        "LastStopReason": "NormalStop",
        "IsMoving": true,
        "IsWatchdogActive": true,
        "WatchdogSecondsRemaining": 81.9,
        "Ready": true,
        "IgnorePhysicalLimitSwitches": true,
        "CheckTime": "2025-09-26T19:21:11.512Z"
      }
    }
  ]
}
```

Use `/health/ready` for readiness probes (filters `"hardware"` tagged checks) and `/health/live` for basic liveness.

---
## 4. Error Representation
The API uses RFC 7807 ProblemDetails objects for failures.
```json
{
  "type": "https://example.com/errors/roof/internal",
  "title": "Service Error",
  "status": 500,
  "detail": "Device not initialized",
  "traceId": "00-a1c7f9c2c2c5413e8d0f7b2a1efc3948-91f0c2d4b6d6f44d-01"
}
```

| Field | Meaning |
|-------|---------|
| type | Stable URI/identifier for the error class |
| title | Human readable summary |
| status | HTTP status code |
| detail | Contextual description |
| traceId | Correlates with server logs / distributed tracing |

---
## 5. Versioning Strategy
- URL segment versioning (currently `v4.0`).
- Additive response fields are considered non-breaking.
- Breaking changes will increment the major version (e.g., `/api/v5.0/...`).

---
## 6. Polling Guidance
| Use Case | Recommended Interval |
|----------|---------------------|
| UI status updates during motion | 500–1000 ms |
| Idle dashboard refresh | 2–5 s |
| Health monitoring / readiness | 10–30 s |

---
## 7. Security & Hardening (Roadmap)
| Concern | Planned Mitigation |
|---------|-------------------|
| Unauthorized motion | Authentication/Authorization (JWT or API key) |
| Replay of motion command | Nonce or short-lived signed tokens |
| Tampering | HTTPS + HSTS |
| Flood / abuse | Rate limiting (429) |

---
## 8. Change Log
| Version | Notes |
|---------|-------|
| 1.1 | Updated for v4 endpoints, payload shape, and health data |
| 1.0 | Initial extraction from `HARDWARE_OVERVIEW.md` |

---
## 9. References
- `HARDWARE_OVERVIEW.md`
- `LOGGING_REFERENCE.md`
- `TROUBLESHOOTING_GUIDE.md`
- `OPERATOR_CHEAT_SHEET.md`

# RoofController REST API Reference (v1.0)

Date: September 26, 2025  
Scope: Roof motion control / status / fault management  
Base Path (default deployment): `/api/v1.0/roof`

> NOTE: If the site is hosted behind a reverse proxy or under a path base, prepend that path accordingly. All responses are JSON (`application/json; charset=utf-8`).

---
## 1. Resource Overview
| Endpoint | Method | Summary | Idempotent | Typical Success | Notes |
|----------|--------|---------|------------|-----------------|-------|
| `/status` | GET | Current status snapshot | YES | 200 | Safe to poll (UI refresh) |
| `/open` | POST | Begin open motion | PARTIAL* | 202 Accepted | Does nothing if already Open/Opening |
| `/close` | POST | Begin close motion | PARTIAL* | 202 Accepted | Does nothing if already Closed/Closing |
| `/stop` | POST | Graceful ramp stop (hold inhibited) | YES | 200 | Always safe; resolves partial state |
| `/clear-fault` | POST | Pulse RLY3 to clear drive fault | NO | 200 | Only valid when Status=Error |

*Open / Close are idempotent regarding direction intent (re‑issuing the same direction while in that direction yields no state change beyond updated timestamps).

---
## 2. Status Model
Representative payload (fields may expand – additive changes only):
```json
{
  "status": "Opening",
  "isReady": true,
  "limits": {
    "isOpen": false,
    "isClosed": false
  },
  "isMoving": true,
  "stopReason": "NormalStop",
  "watchdog": {
    "isActive": true,
    "secondsRemaining": 83.4,
    "lastKickUtc": "2025-09-26T19:05:31.044Z"
  },
  "lastCommandUtc": "2025-09-26T19:05:30.221Z",
  "fault": {
    "isFaulted": false,
    "raw": false
  },
  "telemetry": {
    "atSpeedRun": false
  }
}
```

### 2.1 Enumerations
`status` one of:
```
NotInitialized | Unknown | Opening | Closing | Open | Closed | PartiallyOpen | PartiallyClose | Stopped | Error
```
`stopReason` one of (may be null when moving):
```
None | NormalStop | LimitSwitchReached | EmergencyStop | StopButtonPressed | SafetyWatchdogTimeout | SystemDisposal
```

### 2.2 Field Notes
| Field | Type | Description |
|-------|------|-------------|
| status | string | Current controller state machine status |
| isReady | bool | Ready = Initialized && !Disposed && status != Error |
| limits.isOpen | bool | Logical interpretation of Open limit (polarity abstracted) |
| limits.isClosed | bool | Logical interpretation of Closed limit |
| isMoving | bool | status in {Opening, Closing} |
| stopReason | string|null | Reason associated with last transition to a non‑motion state |
| watchdog.isActive | bool | True while motion watchdog counting |
| watchdog.secondsRemaining | number | Remaining seconds before watchdog would fire |
| watchdog.lastKickUtc | string | ISO8601 UTC timestamp of last heartbeat / kick |
| lastCommandUtc | string | Timestamp of last successfully accepted motion command |
| fault.isFaulted | bool | True when IN3 indicates a drive or chain fault |
| fault.raw | bool | Raw electrical fault line (before interpretation – may be same as `isFaulted`) |
| telemetry.atSpeedRun | bool | True when IN4 (AtSpeed/Run P142=6) asserted |

---
## 3. Endpoint Details
### 3.0 GET /health (Platform Health Aggregation)
The application exposes a standard ASP.NET Core health endpoint returning an aggregate JSON. A RoofController entry is included with extended data.

Example filtered excerpt:
```json
{
  "status": "Healthy",
  "checks": [
    {
      "name": "RoofController",
      "status": "Healthy",
      "data": {
        "IsInitialized": true,
        "IsServiceDisposed": false,
        "Status": "Opening",
        "LastStopReason": "NormalStop",
        "IsMoving": true,
        "IsWatchdogActive": true,
        "WatchdogSecondsRemaining": 81.9,
        "Ready": true,
        "CheckTime": "2025-09-26T19:21:11.512Z"
      }
    }
  ]
}
```
Poll `/health` less frequently than `/status` (recommendation: 10–30 s). Use `/status` for UI animation / progress updates.
### 3.1 GET /status
Returns latest snapshot.

#### Success 200
Body = Status Model

#### Failure
| Code | Condition | Body |
|------|-----------|------|
| 500 | Internal service error / not initialized | ProblemDetails |

Curl example:
```bash
curl -s http://localhost:5136/api/v1.0/roof/status | jq
```

### 3.2 POST /open
Initiates opening motion if safe.

#### Responses
| Code | Meaning |
|------|---------|
| 202 | Accepted; motion starting or already opening |
| 409 | Conflict; already open / at open limit / faulted |
| 500 | Internal failure |

```bash
curl -X POST -s http://localhost:5136/api/v1.0/roof/open | jq
```

### 3.3 POST /close
```bash
curl -X POST -s http://localhost:5136/api/v1.0/roof/close | jq
```
Same response semantics as /open (mirrored for closing direction).

### 3.4 POST /stop
Immediate controlled stop; sets Stop relay and recomputes partial state.

| Code | Meaning |
|------|---------|
| 200 | Stopped (idempotent) |
| 500 | Internal failure |

```bash
curl -X POST -s http://localhost:5136/api/v1.0/roof/stop | jq
```

### 3.5 POST /clear-fault
Pulses fault clear relay (RLY3) after performing an internal emergency stop.

| Code | Meaning |
|------|---------|
| 200 | Fault clear attempted (body includes updated status) |
| 409 | Not currently in Error state (prevent unnecessary pulses) |
| 500 | Internal failure |

```bash
curl -X POST -s http://localhost:5136/api/v1.0/roof/clear-fault | jq
```

---
## 4. Error Representation
The API uses RFC 7807 ProblemDetails style objects.
```json
{
  "type": "https://example.com/errors/roof/fault-active",
  "title": "Roof Controller Fault Active",
  "status": 409,
  "detail": "Cannot clear fault: controller not in Error state.",
  "traceId": "00-a1c7f9c2c2c5413e8d0f7b2a1efc3948-91f0c2d4b6d6f44d-01"
}
```
| Field | Meaning |
|-------|---------|
| type | Stable error classification URI (documentation anchor) |
| title | Short summary |
| status | HTTP status code |
| detail | Human-friendly contextual explanation |
| traceId | Correlates with server logs / distributed tracing |

Typical `type` patterns (suggested):
```
/errors/roof/invalid-state
/errors/roof/fault-active
/errors/roof/not-initialized
/errors/roof/internal
```

---
## 5. Versioning Strategy
- URL segment version (`v1.0`) – only major/minor breaking changes introduce new segment.
- Additive fields in response bodies are non‑breaking.
- Deprecations signaled via `Deprecation` response header for ≥1 minor cycle before removal in next major.

---
## 6. Rate & Polling Guidance
| Use Case | Recommended Interval |
|----------|---------------------|
| UI status polling during motion | 500–1000 ms |
| Idle dashboard refresh | 2–5 s |
| Health monitoring / readiness | 10–30 s |

Excessively tight polling (<250 ms) provides no practical benefit and may inflate log volumes.

---
## 7. Security & Hardening (Future)
| Concern | Mitigation (future) |
|---------|---------------------|
| Unauthorized motion | AuthN/AuthZ layer (JWT / API Key) |
| Replay of motion command | Nonce or short-lived signed tokens |
| Tampering | HTTPS + HSTS |
| Flood / abuse | Basic rate limit (429) |

---
## 8. Change Log
| Version | Notes |
|---------|-------|
| 1.0 | Initial extraction from `HARDWARE_OVERVIEW.md`.

---
## 9. References
- `HARDWARE_OVERVIEW.md` — wiring, state machine, safety philosophy
- `LOGGING_REFERENCE.md` — structured events for correlating API + service internals
