# Troubleshooting

## WPF build fails with access denied in obj/Debug

Known recurring issue in this repo:

`dotnet build` can fail with WPF markup compilation errors because generated files are locked under:

`src/BluetoothMonitor/obj/Debug/net8.0-windows10.0.19041.0/`

Typical files mentioned by the error:

- `MainWindow.g.cs`
- `OverlayWindow.g.cs`
- `BluetoothMonitor_MarkupCompile.cache`

Typical symptoms:

- `MC1000`
- `MSB4018`
- `System.UnauthorizedAccessException`

Likely causes:

- running `BluetoothMonitor.exe`
- lingering debugger attachment
- stale WPF temp project or markup compile process

What to try:

1. stop the app if it is running
2. stop the VS Code or Visual Studio debug session
3. verify no `BluetoothMonitor.exe` process is still alive
4. rebuild

If you cannot clear it safely in the current task, report it as an environment blocker rather than claiming the project is broken by your code change.

## XAML parse exception for app assets

A prior issue in this repo involved a XAML parse failure caused by:

- missing resource path for `assets/appiconsource.png`

Current detected asset files are:

- `src/BluetoothMonitor/Assets/AppIconSource.png`
- `src/BluetoothMonitor/Assets/AppIcon.ico`

If icon-related XAML breaks:

- verify casing and relative resource path
- verify the asset is included as a `Resource`

## Overlay not visible over a game

Expected limitation from the project README:

- borderless/windowed fullscreen works best
- true exclusive fullscreen may render above normal desktop windows
- the app does not inject into games

## Device shows no battery level

Possible causes:

- Windows does not expose battery telemetry for that device
- the device is connected but not battery-reporting
- the device is unsupported by current HID fallback readers
- another application has exclusive access to the HID interface

Expected behavior:

- such devices may still appear with `—` as battery level

## Start with Windows does not persist

Possible causes:

- registry access blocked by policy
- app not launched from the executable path expected by the user

Note:

- `StartupManager.SetEnabled(...)` swallows exceptions, so failure may be silent

## Settings change works temporarily but is not remembered

Possible cause:

- write failure to `%LocalAppData%\\BluetoothMonitor\\settings.json`

Note:

- `AppSettings.Save()` swallows exceptions, so persistence problems may be silent

## Overlay or settings controls appear cropped

Recent UI work has touched:

- settings cards
- overlay position segmented control
- slider rows
- sidebar spacing
- device card stretching

If cropping appears after a UI edit:

- inspect fixed column widths and margins in `MainWindow.xaml`
- validate at 100%, 125%, and 150% display scaling
- check the minimum window size before assuming a content bug
