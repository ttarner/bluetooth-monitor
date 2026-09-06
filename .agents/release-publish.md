# Release and publish flow

## Existing GitHub workflow

Detected workflow:

- `.github/workflows/release.yml`

Trigger:

- push to `main`

## What the workflow does

1. checks out the repository with full history
2. sets up .NET 8
3. restores `BluetoothMonitor.sln`
4. publishes a self-contained single-file `win-x64` build
5. copies `BluetoothMonitor.exe` into `artifacts/package/BluetoothMonitor/`
6. creates a zip asset
7. prepares release metadata and notes
8. creates or updates a GitHub release using `gh`

## Publish command used in CI

```powershell
dotnet publish src/BluetoothMonitor/BluetoothMonitor.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --output artifacts/win-x64
```

## Release naming

Detected current naming logic:

- short SHA: first 7 chars of `GITHUB_SHA`
- date format: `yyyy.MM.dd`
- release tag: `<date>-<shortSha>`
- release title: `<date> (<shortSha>)`

Example shape:

- tag: `2026.06.28-abc1234`
- title: `2026.06.28 (abc1234)`

## Release assets

Detected assets:

- `artifacts/win-x64/BluetoothMonitor.exe`
- `artifacts/BluetoothMonitor-<releaseTag>-win-x64.zip`

Zip naming pattern:

- `BluetoothMonitor-release-tag-arch.zip` conceptually
- actual current pattern in workflow: `BluetoothMonitor-<releaseTag>-win-x64.zip`

Zip contents:

- folder: `BluetoothMonitor`
- file inside: `BluetoothMonitor.exe`

2
Exception thrown: 'BluetoothMonitor.Services.TemperatureTelemetryException' in BluetoothMonitor.dll
BluetoothMonitor.exe (22700): Loaded 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.28\System.Net.Sockets.dll'. Skipped loading symbols. Module is optimized and the debugger option 'Just My Code' is enabled.
BluetoothMonitor.exe (22700): Loaded 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.28\System.Threading.Overlapped.dll'. Skipped loading symbols. Module is optimized and the debugger option 'Just My Code' is enabled.
BluetoothMonitor.exe (22700): Loaded 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.28\System.Net.NameResolution.dll'. Skipped loading symbols. Module is optimized and the debugger option 'Just My Code' is enabled.
BluetoothMonitor.exe (22700): Loaded 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.28\System.Security.Claims.dll'. Skipped loading symbols. Module is optimized and the debugger option 'Just My Code' is enabled.
6
Exception thrown: 'BluetoothMonitor.Services.TemperatureTelemetryException' in BluetoothMonitor.dll
2
Exception thrown: 'BluetoothMonitor.Services.TemperatureTelemetryException' in BluetoothMonitor.dll
BluetoothMonitor.exe (22700): Loaded 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.28\System.Net.Sockets.dll'. Skipped loading symbols. Module is optimized and the debugger option 'Just My Code' is enabled.
BluetoothMonitor.exe (22700): Loaded 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.28\System.Threading.Overlapped.dll'. Skipped loading symbols. Module is optimized and the debugger option 'Just My Code' is enabled.
BluetoothMonitor.exe (22700): Loaded 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.28\System.Net.NameResolution.dll'. Skipped loading symbols. Module is optimized and the debugger option 'Just My Code' is enabled.
BluetoothMonitor.exe (22700): Loaded 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.28\System.Security.Claims.dll'. Skipped loading symbols. Module is optimized and the debugger option 'Just My Code' is enabled.
6
Exception thrown: 'BluetoothMonitor.Services.TemperatureTelemetryException' in BluetoothMonitor.dll
## Release notes generation

The workflow:

- finds the previous tag if available
- groups commit subjects into:
  - Features & Improvements
  - Fixes
- builds `artifacts/release-notes.md`
- includes a compare link when a previous tag exists

## Local publish guidance

If a future agent needs a local publish similar to the VS Code task:

```powershell
dotnet publish src/BluetoothMonitor/BluetoothMonitor.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/win-x64
```

If the goal is to mimic CI more closely:

```powershell
dotnet publish src/BluetoothMonitor/BluetoothMonitor.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --output artifacts/win-x64
```

## Important distinction

Current local VS Code publish task:

- framework-dependent

Current GitHub release workflow:

- self-contained
- single-file

Do not assume they produce identical outputs.
