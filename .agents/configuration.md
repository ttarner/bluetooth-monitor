# Configuration and secrets

## User settings persistence

Detected settings storage:

- directory: `%LocalAppData%\\BluetoothMonitor`
- file: `%LocalAppData%\\BluetoothMonitor\\settings.json`

Defined in:

- `src/BluetoothMonitor/Services/AppSettings.cs`

## Persisted settings currently detected

- `OverlayPosition`
- `StartWithWindows`
- `ShowOverlayOnStartup`
- `KeepRunningInTrayOnClose`
- `OverlayBackgroundOpacity`
- `OverlayTextOpacity`
- `OverlayScale`
- `ShowClockInOverlay`
- `ShowCpuInOverlay`
- `ShowCpuTemperatureInOverlay`
- `ShowGpuTemperatureInOverlay`
- `ShowRamInOverlay`
- `ShowNetworkInOverlay`
- `ShowNetworkOutOverlay`
- `TemperatureDataSource`
- `TemperatureRefreshSeconds`
- `ShowWeatherInOverlay`
- `WeatherLocation`
- `WeatherRefreshMinutes`
- `OverlayWidgetOrder`
- `LowBatteryThreshold`
- `DeviceOverlayVisibility`

## Startup configuration

Detected startup persistence mechanism:

- Registry path: `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run`
- Value name: `BluetoothBatteryMonitor`

Defined in:

- `src/BluetoothMonitor/Services/StartupManager.cs`

When enabled, the stored command is:

- current executable path
- plus the `--startup` argument

## Secrets

Repository secrets files: Not detected.

App secrets, local secret stores, or `.env` files: Not detected.

GitHub Actions uses:

- `secrets.GITHUB_TOKEN`

This is provided by GitHub Actions at runtime and should not be copied into docs, code, or local files.

## What not to commit

- `%LocalAppData%\\BluetoothMonitor\\settings.json`
- local registry exports
- personal or machine-specific paths
- generated release artifacts unless explicitly intended
- any credentials accidentally copied from GitHub or local tooling

## Configuration caveats

- `AppSettings.Save()` swallows exceptions, so a settings bug may present as “works for this session but does not persist.”
- `StartupManager.SetEnabled(...)` also swallows registry exceptions, so a startup problem may not surface visibly in the UI.
- Temperature mode `Auto` is intentionally hybrid: it may keep GPU values from `LibreHardwareMonitor` while filling a missing CPU value from Windows thermal zones.
- Windows thermal zones are fully native and require no user-supplied log file, but they may expose only generic ACPI temperatures on some machines.

## Unknown or not detected

- environment-variable-based app configuration: Not detected
- appsettings.json: Not detected
- user secrets integration: Not detected
- external service credentials: Not detected
