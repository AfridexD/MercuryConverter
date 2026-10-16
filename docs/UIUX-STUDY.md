# Mercury Converter: UI/UX study and improvement plan

Status: **study only. Nothing in the app was changed.** This document is also meant to be the starting brief for the Claude Design redesign.

Screenshots are in `docs/ui-study/`. Test data was removed afterwards.

---

## 1. Summary

The app already feels like a modern Windows app. It uses Mica, rounded cards, real previews, clear per-file states, and results such as "674 KB → 67 KB (−90%)". It stayed responsive while converting 200 photos.

The weak points are not about colors or polish. They are about **flow, feedback at scale, and accessibility**:

| # | Finding | How I know | Priority |
|---|---|---|---|
| 1 | A right-click "(low quality)" run silently became the user's saved quality | Seen in the app after my tests: the slider sat at "Smallest file" | P0 |
| 2 | Below about 800 px wide the settings panel is cut off ("Save pres…") | Screenshot at 760 px | P0 |
| 3 | A screen reader cannot use the app: 9 of 33 interactive controls have no name, file cards read as a code class name, progress bars have no label | Accessibility tree dump | P0 |
| 4 | Pressing Convert when everything is already done gives a confusing result | Code review | P0 |
| 5 | Results are not actionable: no "Show in folder", no remove, no retry | Observed | P1 |
| 6 | At 200 files you cannot see where the work is, and the status line has no count or time left | Screenshot mid-batch | P1 |
| 7 | The settings panel shows every option for every file type, and it needs scrolling at normal window heights | Observed | P1 |
| 8 | Mixed batches (photos plus video) hit errors instead of being handled | Observed ("Only images can be converted to JPEG XL") | P1 |
| 9 | A right-click conversion opens the whole app, which is heavy for a quick job | Observed | P1 |
| 10 | No Settings page. Options are scattered and there is nowhere for licenses or About | Observed | P2 |

**Recommended order:** fix the P0 defects (small, high value), then restructure the main flow (P1), then do the visual redesign in Claude Design on top of a flow that already works. Doing the redesign first would paint over problems that remain.

---

## 2. What was examined

| Evidence | Detail |
|---|---|
| Screenshots | Empty state at 1360, 1000, and 760 px wide; converting; mixed results; large previews; previews off; 200-file batch midway |
| Accessibility tree | Windows UI Automation dump of the running app, 72 elements |
| Contrast | Measured from screenshots (text and background colors sampled) |
| Scale test | 200 photos (about 1 MB each) to WebP, with the previews loading at the same time |
| Guidance | Microsoft Learn pages on progress controls, InfoBar, accessible names, and keyboard interaction (linked in section 11) |
| Code review | The view model and XAML, for behavior that screenshots cannot show |

**Limits of this study**
- Only the dark theme was seen. Light theme, high contrast, 200% text size, and right-to-left languages were **not** tested.
- Comparison with other converter apps is thin. My web searches returned little usable detail, so the pattern notes in section 5 are general knowledge, not verified in this session.
- No real users were involved. Section 9 proposes how to test with them.

---

## 3. Who uses this, and what for

| User | Main job | What they need most |
|---|---|---|
| **Quick converter** (most common) | Right-click one file, pick a format, get the result | Almost no UI. Fast. Clear when it is done and where the file is |
| **Batch photographer** | Shrink or convert dozens or hundreds of photos for web or sharing | Confidence at scale, size estimate, resize, a visible queue, total progress |
| **Video sharer** | Make a video small enough to send or post | A recommended setting, estimated size, a clear speed and CPU trade-off |
| **Power user** | Repeat the same recipe | Presets, advanced options, keyboard shortcuts |

The current window is designed for the last two. The first group, which is probably the largest, has no dedicated path.

---

## 4. Findings

Severity: **P0** broken or blocks someone, **P1** hurts the main flow, **P2** noticeable friction, **P3** polish.

### A. Defects found while testing

**U1. One-off command-line options overwrite saved settings (P0).**
The menu item "To Mp4 (low quality)" starts the app with quality 30. The app saves the current settings before each conversion, so quality 30 and the chosen format became the saved defaults. The next time the user opens the app, the slider is at "Smallest file" for no visible reason.
*Direction:* settings from the menu apply to that run only. Only changes the user makes in the window are saved.

**U2. The properties panel is clipped on narrow windows (P0).**
The panel is a fixed 380 px wide beside a canvas with a 360 px minimum. Below about 800 px the right edge is cut off ("Save pres…", missing drop-down arrows). See `02-narrow-760px-clipped.png`. There is also no minimum window size.
*Direction:* set a minimum window size, and switch to a stacked layout (settings below or in a slide-over pane) when narrow.

**U3. The app is not usable with a screen reader (P0).**
From the accessibility tree:
- 33 interactive controls; **9 have no name**: the three toolbar buttons (Add files, Add folder, View), five drop-downs (Preset, Convert to, Size, Rotate, Speed), and the Quality slider.
- Each file card is announced as `Mercury.Converter.App.ViewModels.FileItem`.
- Progress bars have no name.
- Batch messages ("Finished. 4 converted, 2 failed.") have no announcement set up, so a screen reader user would not be told when work finishes. This is from code review; I did not run Narrator.
Microsoft's guidance says a visible label should also be the accessible name, using `LabeledBy` for form fields, and that names from data-bound lists must be checked after binding.

**U4. Convert with nothing left to do is confusing (P0, from code review).**
If every file is already Done and the user presses Convert again, the app runs an empty batch and reports "Finished. 0 file(s) converted." Nothing explains why.
*Direction:* disable Convert unless there is work, and offer "Convert again" or "Clear" after a finished batch.

### B. Flow and structure

**U5. Results are not actionable (P1).** A finished card shows sizes but offers no "Show in folder", "Open file", "Remove", or "Retry". The saved path exists only in a tooltip. Users cannot find their output without searching.

**U6. Large batches lose the user (P1).** In `07-batch-200-midway.png` the visible cards at the top are all Done, while the active work is far below the fold. The status line says "Converting 200 files…" with no "137 of 200" and no time left. There is no sort, filter, or "jump to active".

**U7. The settings panel does not adapt to the files (P1).** Rotate and flip show for audio. The quality slider shows for lossless formats where it does nothing. The panel is taller than the window at 860 px, so "Save to" sits below the fold. Users meet 20 controls before their first conversion.

**U8. Mixed batches fail late (P1).** Adding photos and a video, then choosing JPEG XL, produces per-file errors afterwards ("Only images can be converted to JPEG XL."). The app should prevent or resolve this before the user presses Convert.

**U9. Right-click conversions open the full window (P1).** A user who right-clicks a file and picks "To Mp4" most likely expects a small progress indicator, not the whole editor. I believe the original File Converter works this way, but I did not re-check it in this study.

**U10. The format list is flat and technical (P1).** One drop-down of 18 mixed items with labels such as "AVIF (smallest images)". There is no "recommended for this", no grouping by file type, and no sense of the trade-off between formats.

**U11. The presets block is heavy (P2).** A drop-down, a name box, Save, Import, and Export are always on screen. Most people use presets rarely. Management actions belong behind a "Manage" dialog.

**U12. The right-click toggle is out of place (P2).** It sits in the command bar like a workflow control, but it is a one-time setting. After it is turned on, the only feedback is a line of text, and the Windows 11 "Show more options" step is easy to miss.

**U13. No Settings or About page (P2).** Thumbnail size, speed mode, graphics-card encoding, default folder, language, and taskbar progress have no single home. There is nowhere to show the third-party license notices that ffmpeg, ImageMagick, and libjxl require.

### C. Feedback and trust

**U14. Output naming is invisible (P2).** Results are written as `name.ext`, `name-converted.ext`, or `name (2).ext`. The user is never told which, or where, before converting.

**U15. The failed state is weak (P2).** A failed card shows a thin line plus text. It has no icon, and raw messages can mention internal tool names and exit codes.

**U16. The waiting animation (P2, keep or tune).** You like the moving bar on queued files. At 200 files, 200 bars animate at once. Microsoft's advice on virtualized lists is about items that are still loading, not about per-file task progress, so it only partly applies. The real concern is visual noise and animation cost. A reasonable compromise is to keep the animation but limit it to the next few items, with a calm static bar for the rest. Windows' "show animations" setting should also be respected.

**U17. The primary button depends on the system accent color (P2).** On this PC the accent appears to be gray, so "Add files" and "Convert" look much like the other buttons and like disabled ones. The primary action should stay obvious whatever the system accent is.

### D. Keyboard, language, and theme

**U18. Keyboard support is minimal (P2).** No shortcuts exist (Ctrl+O, Delete, Enter, Esc, Ctrl+A). File cards are not tab stops, so a keyboard user cannot reach a file. Microsoft recommends a logical tab order, accelerators for common commands, and a consistent focus order.

**U19. Dynamic text is not localized (P2).** Window text uses translation files, but the format names, size and rotation choices, status text, and messages are built in code and are English only.

**U20. Light theme, high contrast, and large text are untested (P2).** Several labels use reduced opacity to look secondary. In the dark theme they pass the contrast check (section 6), but in high contrast mode opacity-based styling can disappear.

### E. What works well (keep)

- Mica material, rounded cards, and consistent controls: it feels native.
- Real previews, generated fast and cached.
- Clear states and clear results ("Done", sizes before and after).
- Safe output: the app never overwrites a file.
- The empty state is clear and friendly.
- Scale: 200 photos in 42 s, the window never froze, and memory stayed near 220 MB.
- All measured text passes the WCAG AA contrast requirement in the dark theme.

---

## 5. Direction for the redesign

These are proposals to review. They are not decisions.

### 5.1 Three ways in, one engine

1. **Quick mode.** From a right-click: a small window listing the files with progress bars, then "Done: Show in folder / Close". It can close itself after a few seconds, and it can send a notification. A link opens the full app.
2. **Main window.** For choosing options, batches, and presets.
3. **Settings.** One page for everything that is not part of a single conversion.

### 5.2 Main window layout (sketch)

```
┌ title bar ─────────────────────────────────────────────── ⚙ Settings ┐
│ [+ Add files] [Add folder]    View ▾   Sort ▾   Show: All ▾           │
├───────────────────────────────────────────┬──────────────────────────┤
│  CANVAS                                   │  INSPECTOR               │
│  cards with previews                      │  1  Convert to           │
│  per-file ⋯ menu: Show in folder, Open,   │      [chips for kind]    │
│  Remove, Retry                            │  2  Quality (smart)      │
│                                           │  3  More options ▸       │
│                                           │  4  Save to              │
├───────────────────────────────────────────┴──────────────────────────┤
│ 12 files · 214 MB → about 60 MB     137 of 200 · 2 min left [Convert] │
└───────────────────────────────────────────────────────────────────────┘
```

### 5.3 Ideas by area

**Format and quality (U7, U8, U10)**
- Show only what fits the files: photo options for photos, video options for video, and so on.
- For mixed batches, show one target per kind ("Photos to WebP, Videos to MP4") so nothing fails late.
- Group formats with a plain recommendation ("Smallest photos: JPEG XL or WebP. Works everywhere: JPEG. Video that plays anywhere: MP4.").
- Quality: show the number and an estimated result size. Hide it for lossless formats.
- Put rarely used options (rotate, flip, advanced video) behind "More options".

**Queue at scale (U5, U6)**
- Header line: "137 of 200 done · 2 min left", plus counts of done, failed, and waiting.
- Sort and filter by state. A "Jump to active" shortcut. Auto-scroll option.
- Per-file ⋯ menu and multi-select (remove several, retry failed).
- After a batch: a bar with "Show in folder", "Convert more", and "Clear".

**Presets (U11)**
- Quick chips for the built-ins at the top of the inspector. "Manage presets" opens a dialog for save, import, and export.

**Trust (U14, U15)**
- Show the output location and naming rule in the inspector ("Saved next to the originals as photo.webp").
- Failed cards: an icon, a short plain message, and a "Retry" action. Technical details go behind "Details".

**Settings page (U12, U13)**
- Right-click menu (with a "Test it" hint), previews, speed mode, graphics-card option, default folder, language, taskbar progress, About, and third-party licenses.

**Visual style (U17)**
- Use a defined brand accent for the primary action, or guarantee a clear primary and disabled difference whatever the system accent is.
- One spacing scale (4, 8, 12, 16, 24), two corner radii (4 and 8), the Windows type ramp.
- Subtle motion: cards fade in when added, progress changes ease. Respect the system animation setting.
- Patterns from Photos (top command bar over a content grid) and Clipchamp (a property inspector beside a canvas) already match the current structure and should be kept. These are general observations, not verified in this session.

### 5.4 Two options for the waiting animation (U16)

| Option | Effect |
|---|---|
| **A. Keep as is** | Every queued card animates. Simple. Gets busy at scale |
| **B. Animate the next few** | Next 4 queued cards animate; the rest show a calm static track. Same feel on small batches, quieter on large ones |

Recommendation: B. It keeps what you liked and removes the downside.

---

## 6. Accessibility plan

**Measured contrast (dark theme):** card title 11.7:1, status text 8.2:1, detail text 5.4:1, empty-state hint 5.4:1, quality label 7.7:1. All pass AA (4.5:1 for normal text). This does not cover light theme or high contrast.

**Names and roles**

| Control | Accessible name |
|---|---|
| Toolbar buttons | "Add files", "Add folder", "View options" |
| Drop-downs | "Preset", "Convert to", "Size", "Rotate", "Speed" |
| Quality slider | "Quality", with value text such as "75 of 100, Balanced" |
| File card | "IMG_001.png, image, converting, 42 percent" |
| Card progress bar | "Progress for IMG_001.png", with the percent as its value |
| Batch summary | Announced when it changes, for example "Finished. 4 converted, 2 failed." |

Rules from Microsoft: use `LabeledBy` to tie visible headings to the control; do not add words like "button" to a name; keep the name short and put detail in the help text; localize names like any other text.

**Keyboard plan**

| Key | Action |
|---|---|
| Ctrl+O | Add files |
| Ctrl+Shift+O | Add folder |
| Delete | Remove selected files |
| Ctrl+A | Select all files |
| Enter | Convert (when focus is not in a control that uses Enter) |
| Esc | Cancel the running conversion |
| F6 | Move between canvas, inspector, and action bar |
| Arrow keys | Move between cards (the grid is one tab stop) |

Initial focus: "Add files" when empty, the first card otherwise. Never start focus on a destructive control.

**Also check:** Narrator end to end, light theme, high contrast, 200% text scale, a 760 px window, a right-to-left language, and the "reduce animations" setting.

**Automation idea:** turn the accessibility tree check used in this study into an automated test that fails when any interactive control has no name. It already ran in about two seconds.

---

## 7. Roadmap

Sizes are rough: S under 1 day, M a few days, L a week or more.

### Phase 0: defects and quick wins (S to M)
- U1 stop one-off command options from being saved.
- U2 minimum window size and a narrow layout.
- U3 names for all controls, cards, and progress bars; announce batch results.
- U4 sensible Convert behavior after a finished batch.
- Show a count in the status line ("137 of 200").
- Add the automated accessibility test.

**Done when:** Narrator reads every control; the window works at 760 px; the saved quality is never changed by a menu action.

### Phase 1: main flow (M to L)
- U7 file-aware inspector with "More options".
- U8 per-kind targets for mixed batches.
- U10 grouped, recommended format picker with an estimated size.
- U5 and U6 per-file menu, post-batch bar, counts, sort and filter.
- U18 keyboard shortcuts and focus order.
- U14 and U15 output-location text and a better failed state.

**Done when:** a first-time user converts a photo and a video in under 30 seconds without help, and a 200-file batch is easy to follow.

### Phase 2: quick mode and settings (M)
- U9 quick-mode window and completion notification.
- U13 Settings page, About, and license notices.
- U12 move the right-click toggle there, with a clear Windows 11 hint.
- U11 presets chips and the Manage dialog.

### Phase 3: visual redesign with Claude Design (L)
Use the brief in section 8 once Phases 0 and 1 are done, so the design starts from a working flow.

### Phase 4: polish (M)
- Light theme and high contrast QA, 200% text, right-to-left.
- Localize the dynamic strings (U19), and have native speakers review the drafts.
- Motion, a first-run tip for the right-click menu, drop-target highlight.

---

## 8. Brief for Claude Design

**Product:** Mercury Converter, a free, local Windows converter for photos, video, GIF, and audio. WinUI 3, Mica, per-user install.

**Users:** see section 3. Optimize first for the quick converter and the batch photographer.

**Screens and states to design (light and dark):**
1. Main window: empty, files added, selection, converting (waiting, active, done, failed, cancelled), finished batch, mixed batch.
2. Narrow window (760 px) and large window (1920 px).
3. Quick mode window: converting, done, error.
4. Settings page and About / licenses.
5. Manage presets dialog.
6. Format picker with recommendations.
7. Per-file menu and failed-file details.
8. First-run right-click tip.

**Constraints:**
- Must be buildable with standard WinUI 3 controls and light styling, not a custom toolkit.
- Minimum window about 720 × 520.
- Text can grow 30 to 40% in other languages, and the layout must mirror for right-to-left.
- Must work in high contrast, so meaning cannot rely on opacity or color alone.
- Brand: a defined accent for the primary action, not tied to the system accent.

**Please deliver:** high-fidelity mock-ups for the screens above, a small token set (colors, spacing, radii, type, motion), component specs for the card, inspector section, and queue header, and an accessibility annotation sheet (reading order, names, focus order).

**Attach:** this study and the screenshots in `docs/ui-study/`.

---

## 9. How to test with people

No usage data is collected by the app, so testing is manual. Five people, four tasks, 20 minutes each:

1. Convert one photo to something smaller (via right-click).
2. Convert 20 photos for a website at a given size.
3. Make a video small enough to email.
4. Find where a converted file was saved.

Record time to first success, wrong turns, and where they hesitate. Add one person who uses Narrator or keyboard only. A good result is at least 4 of 5 completing every task unaided.

---

## 10. Decisions needed

| Question | My recommendation |
|---|---|
| Should right-click conversions open the small quick window by default? | Yes, with a setting to open the full app instead |
| Brand accent color, or follow the system accent? | Brand accent for the primary action only; the rest follows the system |
| Waiting animation: all queued items or the next few? | The next few (option B) |
| Mixed batches: allow one target per file kind? | Yes |
| Add a before/after compare view for photos? | Not now; consider after Phase 2 |
| Where does the right-click toggle live? | Settings, with a short hint in the empty state the first time |

---

## 11. Sources

- [Guidelines for progress controls (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/apps/design/controls/progress-controls)
- [InfoBar (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar)
- [Expose basic accessibility information (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information)
- [Keyboard interactions (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions)
- [Controls and patterns for Windows apps (Microsoft Learn)](https://learn.microsoft.com/windows/apps/design/controls)
