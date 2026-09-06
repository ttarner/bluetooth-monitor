# VS Code integration

## Existing VS Code files

Detected:

- `.vscode/extensions.json`
- `.vscode/tasks.json`
- `.vscode/launch.json`

Do not recreate these unless explicitly asked. They already exist.

## Recommended extensions

Current recommendations from `.vscode/extensions.json`:

```json
{
  "recommendations": [
    "ms-dotnettools.csharp",
    "ms-dotnettools.csdevkit"
  ]
}
```

Practical guidance:

- Prefer installing both the C# extension and C# Dev Kit.
- C# Dev Kit is especially useful for solution-aware project loading and debugging in .NET desktop repos.

## Existing tasks.json

Current `.vscode/tasks.json`:

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "Build [Debug]",
      "command": "dotnet",
      "type": "process",
      "args": [
        "build",
        "${workspaceFolder}/BluetoothMonitor.sln",
        "--configuration",
        "Debug"
      ],
      "problemMatcher": "$msCompile",
      "group": {
        "kind": "build",
        "isDefault": true
      }
    },
    {
      "label": "Build [Release]",
      "command": "dotnet",
      "type": "process",
      "args": [
        "build",
        "${workspaceFolder}/BluetoothMonitor.sln",
        "--configuration",
        "Release"
      ],
      "problemMatcher": "$msCompile",
      "group": {
        "kind": "build",
        "isDefault": true
      }
    },
    {
      "label": "Publish [win-x64]",
      "command": "dotnet",
      "type": "process",
      "args": [
        "publish",
        "${workspaceFolder}/src/BluetoothMonitor/BluetoothMonitor.csproj",
        "--configuration",
        "Release",
        "--runtime",
        "win-x64",
        "--self-contained",
        "false",
        "--output",
        "${workspaceFolder}/artifacts/win-x64"
      ],
      "problemMatcher": "$msCompile"
    }
  ]
}
```

Notes:

- Both Debug and Release tasks are marked as default build tasks. VS Code may only effectively use one default build choice depending on how tasks are surfaced.
- The publish task is framework-dependent, not self-contained.
- The repo also has `Restore`, `Test`, and `Run App` tasks for the common desktop workflow.

## Existing launch.json

Current `.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": ".NET: Bluetooth Monitor",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "Build [Debug]",
      "program": "${workspaceFolder}/src/BluetoothMonitor/bin/Debug/net8.0-windows10.0.19041.0/BluetoothMonitor.exe",
      "cwd": "${workspaceFolder}/src/BluetoothMonitor",
      "stopAtEntry": false
    }
  ]
}
```

## How to use VS Code on this repo

Open the repo root in VS Code:

```powershell
code .
```

Then:

1. allow C# Dev Kit / C# extension to restore and load the solution
2. run `Tasks: Run Task` -> `Restore` when dependencies changed
3. press `Ctrl+Shift+B` for build
4. run `Tasks: Run Task` -> `Test` for automated checks
5. press `F5` to debug with `.NET: Bluetooth Monitor`

## Suggested content guidance

If future work requires editing `.vscode/*`, keep these principles:

- point launch configs at `src/BluetoothMonitor/bin/<Configuration>/net8.0-windows10.0.19041.0/BluetoothMonitor.exe`
- keep `cwd` at `src/BluetoothMonitor`
- keep tasks rooted at the solution or startup project
- prefer `type: "process"` for `dotnet` tasks

## C# Dev Kit guidance

Detected applicability: High.

Why:

- single-solution repo
- desktop app startup project
- existing launch/task setup

Suggested workflow:

- open `BluetoothMonitor.sln` when prompted
- use the Solution Explorer provided by Dev Kit for navigating the WPF project
- if the debugger cannot attach cleanly after a WPF markup build failure, stop the running app, clear the lock condition, and rerun the debug launch

## Not detected

- `.editorconfig`: Not detected
- `Directory.Build.props`: Not detected
- `Directory.Build.targets`: Not detected
