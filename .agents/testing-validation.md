# Testing and validation

## Automated tests

Test projects detected:

- `tests/BluetoothMonitor.Tests/BluetoothMonitor.Tests.csproj`

Current automated coverage includes:

- weather service mapping and friendly failures
- weather view-model refresh and error state
- weather/app-settings normalization
- temperature service fallback mapping and final failure behavior
- temperature view-model stale refresh timing and error state
- overlay widget order normalization
- overlay bounds helper logic
- responsive layout breakpoint logic

## Minimum validation before handing off changes

For non-trivial changes, future agents should aim to validate at these levels:

1. restore/build
2. launch the app
3. manually verify the affected UI or feature path
4. verify no obvious regression in overlay, tray, or startup behavior when relevant

## Build validation commands

```powershell
dotnet restore BluetoothMonitor.sln
dotnet build BluetoothMonitor.sln --configuration Debug
dotnet test BluetoothMonitor.sln --configuration Debug
```

If the default Debug output is locked by a running app or debugger, an isolated validation build is acceptable:

```powershell
dotnet build src/BluetoothMonitor/BluetoothMonitor.csproj -o .tmp-build/app
```

If validating release flow locally:

```powershell
dotnet publish src/BluetoothMonitor/BluetoothMonitor.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/win-x64
```

## Manual run validation

```powershell
dotnet run --project src/BluetoothMonitor/BluetoothMonitor.csproj
```

## Manual UI validation checklist

### Always validate after WPF/XAML changes

- app launches without XAML parse exceptions
- custom title bar renders correctly
- sidebar buttons render and switch views correctly
- no cropped text, clipped buttons, or clipped slider thumbs
- layout still behaves at the current minimum window size
- layout still behaves when maximized
- layout remains readable at 125%, 150%, and 200% Windows scaling if those modes are available

### Dashboard validation

- connected devices appear
- only connected devices are listed
- each device card stretches correctly across available width
- category icon appears on the left
- overlay visibility button works per device
- battery percentage color reflects battery level
- low-battery attention animation still works if affected

### Settings validation

- `Start with Windows` checkbox still updates settings
- `Show overlay at launch` checkbox still updates settings
- `Keep running in tray when closing` checkbox still updates settings
- settings cards switch cleanly between stacked and two-column layout as the window width changes
- opening `Settings` applies the correct initial stacked or two-column layout before any manual resize
- all overlay position buttons work
- overlay position selected state renders correctly
- scale/background/text sliders update values and remain unclipped
- additional widget toggles still persist and affect overlay content
- right-side scrollbars do not sit visually on top of nearby cards

### About validation

- About view opens inside the main window, not as a separate popup window
- sidebar selection updates correctly

### Overlay validation

- `Ctrl+Shift+B` toggles overlay globally
- overlay appears in the selected position
- overlay remains fully inside the selected monitor working area
- overlay repositions correctly on monitors with different Windows scale factors
- overlay honors scale
- overlay honors background opacity
- overlay honors text opacity
- overlay width does not visibly jump because of system widget values beyond expected layout
- overlay CPU/GPU temperature values refresh at startup and again only after the configured stale interval
- hidden devices do not appear in the overlay

### Tray validation

- minimizing hides to tray
- tray double-click reopens the app
- tray menu can open dashboard
- tray menu can toggle overlay
- tray menu can exit the app
- close behavior respects `Keep running in tray when closing`
- dragging the main window remains smooth and does not visibly hitch at the overlay timer cadence

### Startup validation

- enabling `Start with Windows` writes the Run key entry
- startup command includes `--startup`
- startup-hidden behavior keeps the app out of the taskbar initially
- `Show overlay at launch` behavior still works when launched with `--startup`

## Device-specific validation

When touching battery logic, validate with real hardware when possible:

- generic Bluetooth LE device that reports battery via Windows
- DualShock 4 / Wireless Controller over Bluetooth
- SteelSeries Arctis 7+ dongle path if the change can affect it

If hardware is unavailable, document that validation is partial.

## Known validation blocker

Local `dotnet build` can fail because WPF generated files under `src/BluetoothMonitor/obj/Debug/net8.0-windows10.0.19041.0/` are locked, or because `bin/Debug/.../BluetoothMonitor.exe` or `BluetoothMonitor.dll` is still held by the running app/debugger. If that happens, report it clearly rather than claiming a clean build; if needed, validate with an isolated output path.
