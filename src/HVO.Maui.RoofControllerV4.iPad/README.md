# HVO.Maui.RoofControllerV4.iPad

The HVO Roof Controller V4 iPad app is a .NET MAUI client that monitors and controls the observatory roof automation stack. It targets the iOS simulator (iPad) and communicates with the Roof Controller API hosted on the local network.

## Prerequisites

- macOS with Xcode command-line tools and iOS 18.6 simulator runtime installed
- .NET SDK 9.0 (pinned via the repository `global.json`)
- Access to the observatory network, with the Roof Controller API reachable at `http://192.168.2.3:8080/api/v4.0/`
- Trust relationship for the MAUI app bundle identifier on the simulator (handled automatically by the provided launch script)

## Build and Launch

Use the repository launch script to build, install, and start the iPad simulator build:

```bash
./scripts/run-roofcontroller-ipad-sim.sh --configuration Debug
```

This command performs the following:

1. Restores and builds `HVO.Maui.RoofControllerV4.iPad` in the selected configuration.
2. Deploys the resulting `.app` bundle to the simulator with UDID `F878E277-60EC-43CF-90EC-B1C9050549E6`.
3. Boots the simulator if necessary and launches the app, wiring MSBuild logs into the terminal output.

### Using VS Code F5

The default Visual Studio Code launch configuration invokes the same script, so you can press <kbd>F5</kbd> to rebuild and deploy without opening a terminal manually.

## Troubleshooting and Log Collection

During development, it’s often helpful to tail the simulator logs filtered to the Roof Controller app:

```bash
xcrun simctl spawn F878E277-60EC-43CF-90EC-B1C9050549E6 \
  log show --last 5m --style compact \
  --predicate "processImagePath CONTAINS 'RoofController'" \
  --info --debug
```

- Increase or decrease the `--last` window to adjust the log duration.
- Add `--predicate "message CONTAINS 'Error'"` to isolate failure messages.

For continuous monitoring, switch to `log stream` instead of `log show`:

```bash
xcrun simctl spawn F878E277-60EC-43CF-90EC-B1C9050549E6 \
  log stream --style compact \
  --predicate "processImagePath CONTAINS 'RoofController'"
```

Common issues to watch for:

- **HTTP 503 or 404 responses** from the Roof Controller API (usually network/environment configuration).
- **`System.Text.Json.JsonException`** entries indicating payload/enum mismatches. Confirm the API response and client serializer settings in `Services/RoofControllerApiClient.cs`.
- **Simulator permissions prompts** (camera/microphone). Dismiss or accept as needed; the app logs the TCC requests at `info` level.

## Additional Resources

- `scripts/run-roofcontroller-ipad-sim.sh`: build/deploy automation logic.
- `Services/RoofControllerApiClient.cs`: HTTP client implementation and serializer configuration.
- `Configuration/appsettings.json`: Local configuration defaults (API base address, logging levels, etc.).
- `Resources/Splash/splash.svg`: Vector splash artwork shown during cold start (derived from the observatory branding).
- `Resources/Fonts/MaterialSymbolsOutlined.ttf`: Material Symbols Outlined icon font (Apache 2.0) used for tab glyphs.
