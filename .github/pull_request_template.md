## Summary

Introduce a shared I2C register base class and refactor Sequent HAT drivers to use it. Consolidates I2C helpers, standardizes locking, and aligns logging/DI across devices. Public APIs remain unchanged; implementation is simplified and safer.

## Changes

- New `src/HVO.Iot.Devices/Iot/Devices/Common/I2cRegisterDevice.cs` providing:
  - Owned/injected `I2cDevice` constructors, disposal when owned
  - Protected helpers: `ReadByte`, `ReadUInt16`, `ReadUInt32`, `ReadBlock`, `WriteByte`, `WriteUInt16`, `WriteBlock`
  - Shared `Sync` lock for thread-safety
- Refactor `WatchdogBatteryHat` to inherit base:
  - Replaced local I2C helpers with base methods; switched locks to `Sync`
  - Preserved logging via `ILogger<WatchdogBatteryHat>`
  - No public API changes
- Refactor `FourRelayFourInputHat` (SM4rel4in) to inherit base:
  - Removed local helpers and IDisposable; use base helpers and `Sync`
  - Kept nested enums (`LED_MODE`, `LED_STATE`) and public API intact

## Rationale

- DRY I2C access across devices; consistent synchronization and error handling
- Low-allocation `WriteRead` patterns
- Aligns with workspace logging and DI standards

## Compatibility

- Public APIs unchanged; constructors now call the base

## Testing

- Built `HVO.Iot.Devices` in Debug successfully
- Suggested: run `dotnet test src/HVO.Iot.Devices.Tests/HVO.Iot.Devices.Tests.csproj -c Debug`

## Risks

- Low. Register addresses and byte-order preserved; only helper location/locking changed

## Follow-ups

- Migrate remaining I2C devices to `I2cRegisterDevice`
- Consider adding utility helpers (e.g., BCD conversions) for RTC-like devices
