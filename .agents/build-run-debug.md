# Build, run, and debug

## Prerequisites

Detected or documented prerequisites:

- Windows 10 version 2004 / build 19041 or newer
- .NET 8 SDK to build
- .NET 8 Desktop Runtime to run a framework-dependent build

Target framework:

- `net8.0-windows10.0.19041.0`

## Restore

From the repository root:

```powershell
dotnet restore BluetoothMonitor.sln
```

VS Code task:

- `Restore`

## Build

Debug build:

```powershell
dotnet build BluetoothMonitor.sln --configuration Debug
```

If the normal Debug output is locked by a running app or attached debugger, validate compilation with an isolated output path:

```powershell
dotnet build src/BluetoothMonitor/BluetoothMonitor.csproj -o .tmp-build/app
```

Release build:

```powershell
dotnet build BluetoothMonitor.sln --configuration Release
```

## Run

Run the startup project from the repo root:

```powershell
dotnet run --project src/BluetoothMonitor/BluetoothMonitor.csproj
```

VS Code task:

- `Run App`

The `README.md` also documents:

```powershell
dotnet run --project src/BluetoothMonitor
```

## Startup-mode run

The app supports a startup-hidden launch flag used by the app’s Run-key startup entry:

```powershell
dotnet run --project src/BluetoothMonitor/BluetoothMonitor.csproj -- --startup
```

Observed behavior in code:

- `--startup` starts the app hidden
- `MainWindow` still loads
- if `Show overlay at launch` is enabled, the overlay may appear while the main window stays hidden/tray-only

## Debugging

Primary startup executable path for Debug configuration:

`src/BluetoothMonitor/bin/Debug/net8.0-windows10.0.19041.0/BluetoothMonitor.exe`

The repository already contains a VS Code launch config for this executable. See [vscode-integration.md](vscode-integration.md).

## DPI and responsive verification

After UI or overlay changes, validate at least once with:

- the main window restored and maximized
- Windows display scaling at 100% plus at least one fractional value such as 125% or 150%
- the overlay on a secondary monitor if available
- the main window while dragging, if overlay system widgets are enabled

## Manual publish

Framework-dependent publish using the existing VS Code task pattern:

```powershell
dotnet publish src/BluetoothMonitor/BluetoothMonitor.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/win-x64
```

GitHub Actions release publish is different and uses self-contained single-file output. See [release-publish.md](release-publish.md).

## Known local build issue

This repo has a recurring local WPF build problem where generated files under:

`src/BluetoothMonitor/obj/Debug/net8.0-windows10.0.19041.0/`

can become locked, causing errors such as:

- access denied to `MainWindow.g.cs`
- access denied to `OverlayWindow.g.cs`
- access denied to `BluetoothMonitor_MarkupCompile.cache`

When this happens:

- first close any running app instance and attached debugger
- check for a lingering `BluetoothMonitor.exe`
- check whether `BluetoothMonitor.dll` is also still locked by the debugger
- retry the build
- if you only need compilation validation, use `dotnet build src/BluetoothMonitor/BluetoothMonitor.csproj -o .tmp-build/app`

More detail: [troubleshooting.md](troubleshooting.md)

## Startup project

Startup project detected in the solution:

- `src/BluetoothMonitor/BluetoothMonitor.csproj`

No alternate app entry project was detected.
