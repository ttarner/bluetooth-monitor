# Coding guidelines

## General principles

- Preserve existing behavior unless the user explicitly asks for a change.
- Prefer narrow, targeted edits over broad refactors.
- Keep names, event handlers, and bindings stable unless changing them is necessary.
- Follow the existing project style: nullable enabled, implicit usings enabled, straightforward code-behind, lightweight view models.

## Current architectural style

This project is not strict MVVM.

Observed pattern:

- view models are used for bindable device/overlay state
- service classes handle device discovery, persistence, and OS integration
- `MainWindow.xaml.cs` remains the main coordinator for UI events and app orchestration
- `OverlayWindow.xaml.cs` owns overlay-specific runtime behavior

That means future agents should not assume a full MVVM rewrite is desired.

## Editing guidance

- For UI-only changes, prefer XAML edits first.
- Only change code-behind when required by the requested behavior.
- Reuse existing named controls and existing handlers where possible.
- If you introduce a new shared style, place it near similar styles in XAML resources.

## State and persistence

Persisted user settings live in `AppSettings`.

When adding a new persisted option:

1. add the property to `AppSettings`
2. initialize the corresponding control in `MainWindow`
3. update persistence in the relevant change handler
4. apply it to the overlay or service behavior as needed

## Service guidance

- Keep OS integration contained in `Services/`.
- Prefer adding device-specific battery readers as separate service classes rather than bloating `BluetoothBatteryService`.
- Handle Windows or HID API failures defensively; existing code usually catches exceptions and keeps the app usable.

## Error handling

Observed style favors graceful degradation:

- if settings cannot save, app still works for the current session
- if registry access fails, startup behavior simply does not persist
- if a battery watcher fails, the UI remains up and shows a failure message
- if device-specific HID access fails, generic monitoring keeps working

Match that style unless the user asks for stricter error reporting.

## Avoid

- Do not add secrets or machine-specific paths to the repo.
- Do not commit `bin/` or `obj/` cleanup as a code change unless explicitly asked.
- Do not silently rewrite the project into another UI architecture.
- Do not remove fallback support for generic Windows battery reporting when improving device-specific battery logic.

## Existing code patterns worth preserving

- `StringComparison.OrdinalIgnoreCase` for persisted device visibility keys
- `Math.Clamp(...)` around user-configurable slider values
- explicit `Dispatcher` usage when background services update UI-bound collections
- targeted Win32 interop for hotkeys, overlay behavior, and HID access
