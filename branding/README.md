# Mercury Converter — Windows icon package

Mark: **3b Prime Icon** (flat dark-metal owl, Optic Red eyes, Night tile).

## Contents
- `icon/mercury-converter.ico`: multi-size app icon (16, 20, 24, 32, 40, 48, 64, 256). Use for the .exe, installer, shortcuts and window icon.
- `icon/png/`: app icon on its Night tile, 16 to 1024 px.
- `icon/png-unplated/`: owl only, transparent, for the taskbar and system tray.
- `msix/Assets/`: everything an MSIX / Microsoft Store package needs, at scale 100/125/150/200/400, plus targetsize and altform-unplated variants.
- `logo/svg/`: vector masters. `app-icon-master.svg` is the tile; `owl-*.svg` files are the mark on its own (lift = dark backgrounds, metal = light/blue backgrounds, white/black = one-color).
- `logo/png/`: transparent mark at 512 and 1024 px.

## Rules
- At 32 px and below the glow and panel lines are removed on purpose.
- Tile corner radius = 25% of the icon size.
- Optic Red `#F0283C` · Ink `#0D1530` · Signature Blue `#1F4FD6`.

## Package.appxmanifest
```xml
<uap:VisualElements
  DisplayName="Mercury Converter"
  Description="Open-source file converter"
  BackgroundColor="#0D1530"
  Square150x150Logo="Assets\Square150x150Logo.png"
  Square44x44Logo="Assets\Square44x44Logo.png">
  <uap:DefaultTile
    Wide310x150Logo="Assets\Wide310x150Logo.png"
    Square71x71Logo="Assets\SmallTile.png"
    Square310x310Logo="Assets\LargeTile.png"
    ShortName="Mercury" />
  <uap:SplashScreen Image="Assets\SplashScreen.png" BackgroundColor="#0D1530" />
</uap:VisualElements>
```
In `<Properties>`, set `<Logo>Assets\StoreLogo.png</Logo>`.

## Other app types
- WinForms / WPF: set `<ApplicationIcon>icon\mercury-converter.ico</ApplicationIcon>` in the .csproj.
- Electron: `"win": { "icon": "icon/mercury-converter.ico" }` (electron-builder).
- Inno Setup: `SetupIconFile=icon\mercury-converter.ico`.
- Tauri: list `icon/mercury-converter.ico` and `icon/png/app-icon-*.png` under `bundle.icon`.
