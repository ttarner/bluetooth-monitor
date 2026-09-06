# UI guidelines

## Current UI framework

Detected UI framework:

- WPF

Main UI files:

- `src/BluetoothMonitor/MainWindow.xaml`
- `src/BluetoothMonitor/OverlayWindow.xaml`
- `src/BluetoothMonitor/App.xaml`

## Current visual direction

The app currently uses a custom dark-mode UI with:

- custom title bar
- custom sidebar navigation
- rounded cards
- teal/cyan accent color
- dark navy/charcoal gradients
- white or muted blue-gray typography

This is not a default WPF window chrome design.

## Main window constraints

Detected from `MainWindow.xaml`:

- width: `900`
- height: `680`
- `MinWidth="900"`
- `MinHeight="680"`
- `WindowStyle="None"`
- `ResizeMode="CanResize"`
- `Background="Transparent"`
- custom `WindowChrome`
- explicit rounded clipping with an 8 px visual corner radius

Agents should assume layout regressions are easy to introduce near the minimum size, in maximized mode, and at 125%-200% display scaling.

## View structure

Current content areas inside `MainWindow`:

- `DashboardView`
- `SettingsView`
- `AboutView`

Current sidebar buttons:

- `DashboardSidebarButton`
- `SettingsSidebarButton`
- `AboutSidebarButton`

Current sidebar layout intent:

- Dashboard at top
- Settings near bottom
- About last

## Overlay UI constraints

Detected from `OverlayWindow.xaml`:

- `SizeToContent="WidthAndHeight"`
- `MinHeight="96"`
- `MaxHeight="400"`
- `WindowStyle="None"`
- `AllowsTransparency="True"`
- `Topmost="True"`
- `ShowActivated="False"`

Overlay behavior expectations:

- visually minimal
- does not steal focus
- click-through
- stable enough for gaming overlays

## Existing UX patterns to preserve

- device cards expand across the available content width
- dashboard content starts visually from the left
- settings are grouped into consistent cards
- About is a view, not a popup window
- selected sidebar and segmented options have a stronger teal-highlighted state
- per-device overlay visibility uses an icon-based toggle affordance

## UI editing rules

- Do not rename existing named controls unless necessary.
- Do not break existing event hookups.
- Avoid accidental clipping from fixed widths or overly tight grids.
- If a control uses shared style resources, update the style instead of applying one-off visual properties everywhere.
- When changing segmented controls or sliders, verify selected, hover, and unselected states.

## Manual UI regression hotspots

Known sensitive areas based on recent changes:

- overlay position segmented buttons
- adaptive settings single-column vs two-column layout
- settings card widths and cropping
- slider value columns
- sidebar spacing and bottom alignment
- device card width adaptation
- compact device-card density on the dashboard
- overlay width and widget text stability
- maximized custom-chrome bounds on non-primary monitors
- mixed-DPI monitor moves while the overlay is visible

## Responsive and DPI guidance

Current expectations:

- prefer `Grid`, `Auto`, and `*` sizing over fixed card widths
- keep scroll viewers enabled for vertically long views
- keep enough right-side breathing room so vertical scrollbars do not visually crowd nearby cards
- use layout rounding so borders and text stay crisp at fractional scale factors
- preserve readable layout at 100%, 125%, 150%, 175%, and 200% Windows scaling
- avoid assuming the primary monitor work area for overlay placement
- treat overlay bounds as monitor-specific and DPI-aware

When changing the custom window or overlay:

- remember that WPF `Border.CornerRadius` does not clip child content on its own
- preserve the explicit rounded clipping path on the main shell unless you intentionally replace it
- verify maximized bounds against the monitor working area
- verify the main window remains usable on tighter logical widths caused by high DPI
- avoid introducing new hardcoded monitor or pixel assumptions

## Icon guidance

Observed current icon sources:

- `Segoe MDL2 Assets` for shell/sidebar glyphs
- emoji/text glyphs for some device and status affordances
- vector shapes in XAML for some segmented controls

When possible:

- prefer built-in WPF-friendly glyphs or vector shapes
- avoid adding external icon libraries for small UI tweaks

## Accessibility and readability

No explicit accessibility framework or automation strategy was detected.

Still, future agents should aim for:

- readable contrast in dark mode
- unclipped text at 100%, 125%, and 150% display scaling
- clear selected/unselected states
