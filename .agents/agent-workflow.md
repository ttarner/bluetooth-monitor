# Agent workflow

## First-pass workflow

When starting work in this repo:

1. read [project-overview.md](project-overview.md)
2. inspect the exact files relevant to the request
3. prefer narrow edits
4. validate with restore/build/run as far as the local environment allows
5. report any environment blockers clearly

## Repository-specific expectations

- This is a single-project WPF solution.
- Most feature work touches `MainWindow.xaml`, `MainWindow.xaml.cs`, `OverlayWindow.xaml`, view models, or services.
- The codebase already mixes WPF binding with code-behind orchestration; do not force a full architectural rewrite.
- UI tweaks often require only XAML changes.

## Safe change strategy

### For UI-only requests

- inspect `MainWindow.xaml` or `OverlayWindow.xaml`
- keep existing control names and handlers
- avoid moving unrelated layout parts
- validate for clipping and alignment

### For behavior changes

- find whether the behavior lives in:
  - `MainWindow.xaml.cs`
  - `OverlayWindow.xaml.cs`
  - `Services/*`
  - `Models/*`
- keep persistence flow in sync if the behavior is user-configurable

### For battery-related work

- check `BluetoothBatteryService` first
- then inspect `PlayStationBatteryService` and `SteelSeriesBatteryService`
- preserve generic Windows-device behavior while improving device-specific readers

## Validation expectations

Prefer this order:

1. `dotnet restore BluetoothMonitor.sln`
2. `dotnet build BluetoothMonitor.sln --configuration Debug`
3. `dotnet run --project src/BluetoothMonitor/BluetoothMonitor.csproj`
4. manually validate the changed feature

If step 2 is blocked by the known WPF file-lock issue:

- say so explicitly
- do not imply a clean build happened

## Communication expectations for future agents

- lead with what changed
- cite actual file paths
- distinguish confirmed facts from unknowns
- if a build or run step is blocked by the environment, say that plainly

## Do not invent

- extra test coverage that does not exist
- packages or frameworks not present in the repo
- release behavior not present in `.github/workflows/release.yml`
- hidden settings or secret stores not present in source

## High-risk areas

- `MainWindow.xaml` sizing and clipping regressions
- WPF resource paths
- tray icon shutdown/disposal flow
- overlay hotkey registration and cleanup
- startup Run-key behavior
- device-specific HID access code

## Useful commands

```powershell
dotnet restore BluetoothMonitor.sln
dotnet build BluetoothMonitor.sln --configuration Debug
dotnet run --project src/BluetoothMonitor/BluetoothMonitor.csproj
dotnet publish src/BluetoothMonitor/BluetoothMonitor.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/win-x64
```

## When changing docs

Keep `.agents/` grounded in the repository state. If repo structure, tasks, launch configs, or release flow change, update the corresponding markdown here.
