# Prompt for Claude Design

Paste everything between the lines into Claude Design. Attach these files with it:

- `docs/UIUX-STUDY.md` (background and findings)
- all seven images in `docs/ui-study/` (what the app looks like today)

Before sending, check the "Assumptions" section and change anything you disagree with.

---

## The brief

I need a redesign of **Mercury Converter**, a free, open-source Windows app that converts photos, GIFs, video, and audio on the user's own computer. Nothing is uploaded. It is built with WinUI 3 (Windows App SDK) and currently uses a Mica background, rounded cards, and a Photos/Clipchamp-like layout: a canvas of file cards on the left and a properties panel on the right.

The attached screenshots show the current app. The attached study explains what is wrong with it and why. Please read both first.

### Who it is for
1. **Quick converter** (most common): right-clicks one file in Explorer, picks a format, wants it done. Not technical.
2. **Batch photographer**: shrinks or converts 20 to 500 photos for a website or sharing.
3. **Video sharer**: makes a video small enough to send.
4. **Power user**: reuses presets and advanced options.

Design first for 1 and 2. Everything must make sense to someone who has never heard of "codecs" or "bitrate".

### What to design
Please give me high-fidelity designs, in light and dark, for:

1. **Main window**, with these states: empty; files added; one or more selected; converting (queued, active, done, failed, cancelled); finished batch; mixed batch (photos and video together).
2. **Narrow layout** at 760 px wide, and a large layout at 1920 px.
3. **Quick-convert window** for right-click conversions: converting, done, error.
4. **Settings page**, including About and third-party licenses.
5. **Format picker** that groups formats by file type and recommends one ("smallest photos", "plays everywhere").
6. **Inspector panel** that changes with the selected files (photo, video, audio, mixed), with rarely used options under "More options".
7. **Queue header** for large batches: "137 of 200 done · 2 min left", counts, sort, filter, jump to active.
8. **Per-file menu** and **failed-file** treatment (icon, plain message, Retry, details).
9. **Manage presets** dialog, plus quick preset chips.
10. **First-run tip** for turning on the right-click menu.

### Problems the design must solve
- The settings panel is long and shows irrelevant options (for example Rotate for audio).
- Mixed batches fail late. Each file kind needs its own target format.
- At 200 files you cannot see where the work is.
- Finished files have no "Show in folder", remove, or retry.
- The main button looks the same as the others when the system accent color is gray.
- A screen reader cannot use the app today. The design should include reading order, names, and focus order.

### Constraints
- Build with standard WinUI 3 controls and light styling. No custom control toolkit.
- Windows 10 (1809+) and Windows 11. Minimum window about 720 × 520.
- Support light theme, dark theme, and high contrast. Meaning must never depend on color or opacity alone.
- Text can be 30 to 40% longer in other languages (Spanish, French, German, Portuguese so far). The layout must mirror for right-to-left.
- Respect the system "show animations" setting.
- Keep the waiting animation on queued files that the user liked, but design a calmer version for large queues (animate only the next few).

### What I want back
1. The screens listed above, with states.
2. A small set of design tokens: colors (including one brand accent for the primary action), spacing (4, 8, 12, 16, 24), corner radii (4 and 8), type ramp, and motion.
3. Component specs for: file card, inspector section, queue header, per-file menu, and progress states.
4. An accessibility sheet: accessible names, reading order, focus order, keyboard shortcuts.
5. A short note of anything you changed on purpose, and anything you would like me to decide.

### Assumptions (change these if you disagree)
- Right-click conversions open the small quick window by default, with a setting to open the full app instead.
- The brand accent is used for the primary action only. Everything else follows the system accent.
- Mixed batches get one target per file kind.
- The right-click menu toggle moves to Settings, with a short hint the first time.
- No before/after compare view yet.
- The app collects no usage data.

### Please ask me first if
Anything in the brief is unclear or conflicts with the screenshots. I would rather answer a few questions than get designs for the wrong problem.
