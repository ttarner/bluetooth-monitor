# Dependencies

## Project-level dependencies

From `src/BluetoothMonitor/BluetoothMonitor.csproj`:

- SDK: `Microsoft.NET.Sdk`
- Target framework: `net8.0-windows10.0.19041.0`
- WPF enabled
- Windows Forms enabled

## NuGet package references

Explicit `<PackageReference>` items: Not detected.

This project currently appears to rely on the .NET SDK, the Windows desktop framework, and Windows APIs rather than repo-declared third-party NuGet packages.

## Important namespaces and platform APIs in use

Observed in source:

- `System.Windows` / WPF
- `System.Windows.Forms` via alias `Forms` for tray icon support
- `Windows.Devices.Enumeration` for Bluetooth device discovery
- `Microsoft.Win32` for Run-key startup registration
- `System.Net.NetworkInformation` for overlay network stats
- Win32 interop (`user32.dll`, `kernel32.dll`, `hid.dll`, `setupapi.dll`) for:
  - hotkeys
  - transparent overlay window styles
  - HID device access
  - Sony controller enumeration
  - SteelSeries dongle enumeration

## Device-specific support layers

Detected specialized battery handling:

- Sony DualShock 4 via `PlayStationBatteryService`
- SteelSeries Arctis 7+ family via `SteelSeriesBatteryService`

Generic Windows battery handling:

- `BluetoothBatteryService`

## Assets

Detected app assets:

- `src/BluetoothMonitor/Assets/AppIcon.ico`
- `src/BluetoothMonitor/Assets/AppIconSource.png`

Documentation images:

- `doc/images/bbm-home.png`
- `doc/images/bbm-settings.png`
- `doc/images/bbm-overlay.png`
- `doc/images/bbm-tray.png`

## Tooling dependencies

Detected tooling expectations:

- .NET 8 SDK
- VS Code with:
  - `ms-dotnettools.csharp`
  - `ms-dotnettools.csdevkit`
- GitHub CLI `gh` in the release workflow runner environment

## Unknown or not detected

- test frameworks: Not detected
- linters/formatters beyond SDK defaults: Not detected
- analyzers beyond SDK defaults: Not detected
- dependency injection framework: Not detected
