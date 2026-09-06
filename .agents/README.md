# Bluetooth Battery Monitor agent docs

This folder is a repo-local handbook for future AI coding agents working on this project.

Start here:

- [project-overview.md](project-overview.md)
- [build-run-debug.md](build-run-debug.md)
- [vscode-integration.md](vscode-integration.md)
- [testing-validation.md](testing-validation.md)
- [coding-guidelines.md](coding-guidelines.md)
- [ui-guidelines.md](ui-guidelines.md)
- [configuration.md](configuration.md)
- [dependencies.md](dependencies.md)
- [release-publish.md](release-publish.md)
- [troubleshooting.md](troubleshooting.md)
- [agent-workflow.md](agent-workflow.md)

Quick facts:

- Solution: `BluetoothMonitor.sln`
- Startup project: `src/BluetoothMonitor/BluetoothMonitor.csproj`
- App type: Windows desktop app
- UI framework: WPF (`<UseWPF>true</UseWPF>`)
- Extra desktop dependency: Windows Forms (`<UseWindowsForms>true</UseWindowsForms>`) for tray icon support
- Target framework: `net8.0-windows10.0.19041.0`
- Test project: `tests/BluetoothMonitor.Tests/BluetoothMonitor.Tests.csproj`
- GitHub Actions release workflow: `.github/workflows/release.yml`
- VS Code config present: `.vscode/extensions.json`, `.vscode/tasks.json`, `.vscode/launch.json`

Known high-value context from recent work:

- The UI has been heavily redesigned into a dark custom-shell dashboard with `Dashboard`, `Settings`, and `About` views inside `MainWindow`.
- Sidebar order is currently Dashboard at the top, with Settings and About grouped at the bottom.
- Overlay positions currently support six options: `TopLeft`, `TopRight`, `CenterLeft`, `CenterRight`, `BottomLeft`, `BottomRight`.
- The repo has a recurring local WPF build issue where generated files under `src/BluetoothMonitor/obj/Debug/net8.0-windows10.0.19041.0/` can become locked during `dotnet build`.
- Recent UI work added responsive settings layout behavior plus DPI-aware overlay placement helpers and tests.
- CPU and GPU temperatures now use native app-managed telemetry paths only: `LibreHardwareMonitor` first, with optional Windows thermal-zone fallback and no user-supplied log file integration.
- Recent custom-window work standardized the main shell to an 8 px rounded shape with explicit clipping and DWM corner override to avoid black corner artifacts.
- Overlay system-widget sampling was moved off the UI thread to reduce visible hitching while dragging the main window.

If you only have time for one file after this one, read [agent-workflow.md](agent-workflow.md).
