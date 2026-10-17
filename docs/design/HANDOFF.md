# Handoff: Mercury Converter — Windows app UI

## Overview
A full Windows 11 desktop UI for Mercury Converter, an open-source batch file-format converter (repo: AfridexD/MercuryConverter). It covers the main window (empty state → mixed batch → format picker → converting → results), the options inspector, Settings, Manage Presets, the right-click tip dialog, the file-problem dialog and the Quick Convert overlay that opens from the Explorer right-click menu.

## About the design files
The HTML files here are **design references**: prototypes that show the intended look and behaviour. They are not production code. Rebuild them in the app's real stack. For a native Windows app, WinUI 3 / Windows App SDK is the natural fit, because the design follows WinUI/Fluent conventions. Open the `.dc.html` files in a browser, keeping `support.js` next to them. `Mercury Converter App (standalone).html` works offline.

## Fidelity
**High fidelity.** Colours, type, spacing, radii and copy are final. Match them exactly, using native WinUI controls wherever they exist.

## Brand rules
- The owl app icon appears **only** in the title bar (16 px) and the taskbar. Everything else looks native to Windows.
- Accent: Signature Blue `#1F4FD6` (light). In dark theme the accent is `#7FA3FF`, with `#0D1530` text on accent.
- Typeface: Segoe UI Variable (Text for body, Display for titles 28 px and up). Icons: Segoe Fluent Icons. The prototype uses Material Symbols Outlined (weight 300) as a browser stand-in; map each one to its Segoe Fluent equivalent.

## Screens / views
### 1. Main window
- Custom title bar, 32 px tall: 16 px icon, app name at 12 px, standard caption buttons 46×32.
- Two columns at widths of 900 px and up: a file area (flex 1) and the Options inspector (360 px; 400 px at 1700 px and up). Below 900 px the inspector becomes an overlay pane opened by a button and closed with a "Close options" button.
- **Empty state:** a centred drop target with the primary button "Add files" and the secondary button "Add folder" (32 px tall, 14 px horizontal padding, 4 px radius, 8 px gap).
- **Batch:** a grid of file cards (8 px radius, `--card` background, 1 px `--stroke` border). A selected card has a 2 px accent inset border and a 20×20 accent check in the top-left corner. While a file is converting, a 3 px progress bar appears at the bottom of its card.
- **Filter tabs** (role tablist, 6 px gap): All, Converting, Done, Problems, each with a count.
- **Overall progress:** a 4 px bar, 2 px radius, `--track` behind an `--accent` fill, animating width over 200 ms linear.
- **Command bar** (bottom right): Clear list, Cancel (Esc) while running, and the primary "Convert" (min-width 140, Enter). When nothing can be converted, Convert is disabled and uses `--disabledFill` / `--disabledText`.
- **Results:** each card has a ⋯ menu (Show in folder, Retry, Remove). A batch summary bar shows the totals and the space saved.

### 2. Options inspector
- **Presets:** chips plus a "Manage" link button.
- **Format picker** (listbox): a "Recommended" group, then the other formats grouped by file type. The selected row gets a 3 px accent pill at its left edge and a selected background.
- **Quality slider** (10–100, step 5) for lossy formats, with "Smaller file" / "Better quality" labels under it.
- **Size hint** on a soft-accent background (`--accentSoft`), with the checkbox "Never make a file larger than the original".

### 3. Settings
A full page of setting cards: 6 px radius, 22 px icon, title and 12 px secondary line, control on the right. Toggle switches are 40×20 with a 12 px thumb that moves over 150 ms. Sections include Appearance, Animations (follows the Windows setting), Performance, the right-click menu, and Third-party licences (each with a View action).

### 4. Dialogs
- **Manage Presets:** a list with Import…, Export… and Done (primary).
- **Right-click tip:** explains how to turn on the Explorer menu entry. Buttons: "Turn on" (primary) and "Not now".
- **File problem:** explains the damage. Buttons: Retry (primary), Copy details, Close.
- All dialogs sit on a smoke layer (`--smoke`). Footer: `--layer` background, top border `--stroke`, 20/24 px padding, buttons flex 1 with an 8 px gap.

### 5. Quick Convert overlay (`Mercury Quick Convert.dc.html`)
- 360 px wide, opens next to the cursor, cannot be resized, stays on top until the task ends. Esc closes it; Enter converts.
- Title bar 32 px. File row: 40×40 thumbnail, name in semibold, meta at 12 px.
- **Pick:** a listbox of 36 px rows showing format, a "Recommended" badge and the estimated size. Footer: Convert (primary) and More options… (opens the main window).
- **Converting:** label, percentage and a 4 px bar. Footer: Cancel.
- **Done:** an `--okSoft` message with the output name and size change. Buttons: Show in folder and Close.
- **Problem:** an `--errSoft` message. Buttons: "Save first 00:41" (partial save) and Open in Mercury.

## Interactions and behaviour
- Theme follows the system, with a manual override in Settings.
- Respect `prefers-reduced-motion` / the Windows animation setting: the indeterminate "waiting" shimmer (the `mcWait` keyframe, translateX −110% → 260%) is replaced by a static bar.
- Keyboard: Enter starts Convert, Esc cancels or closes, Space selects a card, and all focus is visible (WinUI focus visual).
- Button labels must never wrap (`white-space: nowrap`, no shrinking). An earlier bug caused wrapping.

## State
`files[]` (each with name, type, size, status: queued | active | done | error, pct), `selection`, `format`, `quality`, `neverLarger`, `filter`, `running`, `overlay` (none | picker | details | presets | tip), `theme`, `paneOpen` (narrow widths).

## Design tokens
Radii: buttons and inputs 4, setting cards 6, file cards and windows 8. Control heights: 32 (standard), 28 (compact). Shadow: see `--shadow`.
Type: body 14/20, secondary 13/19, caption 12/16, section labels 12 semibold, page titles 28/36 semibold.

### Light
- `--bg`: `#F2F3F6`
- `--winStroke`: `rgba(0,0,0,0.12)`
- `--layer`: `#FAFAFB`
- `--card`: `#FFFFFF`
- `--cardHover`: `#F6F7F9`
- `--stroke`: `rgba(0,0,0,0.08)`
- `--strokeStrong`: `rgba(0,0,0,0.16)`
- `--text`: `#1A1B20`
- `--text2`: `#464953`
- `--text3`: `#5E626D`
- `--ctrl`: `#FFFFFF`
- `--ctrlHover`: `#F6F7F9`
- `--ctrlStroke`: `rgba(0,0,0,0.12)`
- `--subtleHover`: `rgba(0,0,0,0.05)`
- `--input`: `#FFFFFF`
- `--accent`: `#1F4FD6`
- `--accentHover`: `#1A43B8`
- `--onAccent`: `#FFFFFF`
- `--accentText`: `#1F4FD6`
- `--accentSoft`: `rgba(31,79,214,0.08)`
- `--disabledFill`: `rgba(0,0,0,0.08)`
- `--disabledText`: `rgba(0,0,0,0.40)`
- `--track`: `rgba(0,0,0,0.12)`
- `--ok`: `#0F7B0F`
- `--err`: `#C42B1C`
- `--okSoft`: `#DFF6DD`
- `--errSoft`: `#FDE7E9`
- `--flyout`: `#FFFFFF`
- `--smoke`: `rgba(0,0,0,0.3)`
- `--shadow`: `0 8px 32px rgba(0,0,0,0.16)`

### Dark
- `--bg`: `#1C1D22`
- `--winStroke`: `rgba(255,255,255,0.10)`
- `--layer`: `#24262C`
- `--card`: `#2C2E35`
- `--cardHover`: `#32353D`
- `--stroke`: `rgba(255,255,255,0.07)`
- `--strokeStrong`: `rgba(255,255,255,0.14)`
- `--text`: `#FFFFFF`
- `--text2`: `#C8CAD2`
- `--text3`: `#A2A6B1`
- `--ctrl`: `rgba(255,255,255,0.06)`
- `--ctrlHover`: `rgba(255,255,255,0.10)`
- `--ctrlStroke`: `rgba(255,255,255,0.10)`
- `--subtleHover`: `rgba(255,255,255,0.06)`
- `--input`: `rgba(255,255,255,0.05)`
- `--accent`: `#7FA3FF`
- `--accentHover`: `#9DB8F2`
- `--onAccent`: `#0D1530`
- `--accentText`: `#9DB8F2`
- `--accentSoft`: `rgba(127,163,255,0.14)`
- `--disabledFill`: `rgba(255,255,255,0.08)`
- `--disabledText`: `rgba(255,255,255,0.45)`
- `--track`: `rgba(255,255,255,0.16)`
- `--ok`: `#6CCB5F`
- `--err`: `#FF99A4`
- `--okSoft`: `#263526`
- `--errSoft`: `#442726`
- `--flyout`: `#2C2E35`
- `--smoke`: `rgba(0,0,0,0.5)`
- `--shadow`: `0 8px 32px rgba(0,0,0,0.5)`

## Assets
- `assets/app-icon-32.png`, `app-icon-64.png`: the title-bar icon.
- `Mercury Converter - Windows Package/`: the .ico file, plated and unplated PNG sizes, logo SVG/PNG and the MSIX asset set. Use these for the taskbar, Start menu and installer.

## Files
- `Mercury Converter App.dc.html`: the main interactive prototype (all screens, theme toggle).
- `Mercury Converter App (standalone).html`: an offline copy of the same.
- `Mercury Quick Convert.dc.html`: the overlay states, light and dark.
- `Mercury Converter Brand.dc.html`: the brand sheet.
- `support.js`: the runtime the .dc.html files need.

## Not yet designed
The full state board (every screen × light/dark × 760/1920 px), a standalone tokens sheet, component specs, the accessibility matrix, and a changelog of departures from WinUI defaults. Use the tokens and rules above until they exist.
