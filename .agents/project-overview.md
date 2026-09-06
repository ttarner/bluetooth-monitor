# Project overview

## What this app does

Bluetooth Battery Monitor is a Windows desktop app that shows battery levels for connected Bluetooth devices and selected wireless USB peripherals. It also provides a lightweight overlay intended for borderless or windowed-fullscreen gaming scenarios.

The app currently supports:

- connected-device battery listing in the main dashboard
- optional always-on-top click-through overlay
- system overlay widgets: clock, CPU, CPU temperature, GPU temperature, RAM, network in, network out
- tray icon behavior
- start-with-Windows support
- special battery readers for some devices when Windows does not expose battery correctly

## Solution and project structure

Top-level structure detected:

- `BluetoothMonitor.sln`
- `src/BluetoothMonitor/`
- `.vscode/`
- `.github/workflows/`
- `doc/images/`

Key source folders:

- `src/BluetoothMonitor/`
  - `App.xaml`, `App.xaml.cs`
  - `MainWindow.xaml`, `MainWindow.xaml.cs`
  - `OverlayWindow.xaml`, `OverlayWindow.xaml.cs`
  - `Models/`
  - `Services/`
  - `Behaviors/`
  - `Assets/`

## Detected platform and app model

From `src/BluetoothMonitor/BluetoothMonitor.csproj`:

- Output type: `WinExe`
- Target framework: `net8.0-windows10.0.19041.0`
- UI framework: WPF
- Windows Forms enabled: yes
- Nullable: enabled
- Implicit usings: enabled
- Application manifest: `app.manifest`
- Application icon: `Assets/AppIcon.ico`

## Runtime architecture

### Application entry

`App.xaml.cs` is responsible for:

- enforcing single-instance behavior via mutex
- waking the existing instance with an event wait handle
- passing `--startup` behavior through startup args
- creating `MainWindow`

### Main window

`MainWindow.xaml` and `MainWindow.xaml.cs` host the primary app shell.

Current main views are:

- `DashboardView`
- `SettingsView`
- `AboutView`

`MainWindow` also coordinates:

- tray icon lifecycle
- overlay show/hide
- startup option persistence
- hotkey registration (`Ctrl+Shift+B`)
- Bluetooth watcher startup/refresh
- PlayStation and SteelSeries auxiliary battery services

### Overlay window

`OverlayWindow.xaml` and `OverlayWindow.xaml.cs` provide:

- transparent always-on-top overlay window
- click-through behavior via Win32 extended window styles
- position handling for six positions
- overlay scaling
- background and text opacity handling
- optional system widget rendering

### Services

Detected services:

- `BluetoothBatteryService`
  - Windows device enumeration and battery property reading
- `PlayStationBatteryService`
  - DS4 HID battery parsing when Windows battery data is unavailable or incomplete
- `SteelSeriesBatteryService`
  - Arctis 7+ dongle status reader
- `SystemStatsService`
  - clock, CPU, RAM, and network sampling
- `TemperatureTelemetryService`
  - temperature source orchestration for `Auto`, `LibreHardwareMonitor`, and `Windows thermal zones`
- `LibreHardwareMonitorTemperatureSource`
  - primary CPU/GPU sensor reader
- `WindowsThermalZoneTemperatureSource`
  - native CPU fallback based on Windows thermal-zone counters
- `StartupManager`
  - current-user Run-key registration
- `AppSettings`
  - JSON persistence in LocalAppData

### Models

Detected models:

- `BluetoothDeviceViewModel`
- `OverlayViewModel`
- `SystemOverlayWidgetViewModel`
- `TemperatureOverlayViewModel`
- `BluetoothDeviceCategory`
- `OverlayPosition`
- `TemperatureDataSourceMode`

## UI architecture summary

The current UI is not a multi-window app for settings/about. Instead:

- one custom chrome main window contains dashboard, settings, and about sections
- the overlay is a separate window
- tray interactions reopen or hide the main window

Recent design direction visible in XAML:

- premium dark theme
- custom title bar
- left sidebar navigation
- rounded cards
- teal accent styling
- responsive card/list behavior within the fixed minimum window size
- explicit 8 px rounded main-window clipping to keep the custom shell visually clean on Windows

## Test layout

Detected test project:

- `tests/BluetoothMonitor.Tests/BluetoothMonitor.Tests.csproj`

Current test coverage includes weather, temperature telemetry, app-settings normalization, overlay layout helpers, and responsive breakpoint logic.
