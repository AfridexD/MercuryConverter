# Building Mercury Converter

This page is for people who want to build the app from source, run the tests, or make an installer.

## What you need

| Tool | Why | Get it |
|---|---|---|
| Windows 10 (2004) or 11, 64-bit | The app is Windows only | |
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Builds the app and the tests | `winget install Microsoft.DotNet.SDK.10` |
| [7-Zip](https://www.7-zip.org/) and [ImageMagick](https://imagemagick.org/) | Used by the tool-fetching script | `winget install 7zip.7zip ImageMagick.ImageMagick` |
| [Inno Setup 6](https://jrsoftware.org/isinfo.php) | Only to build the installer | `winget install JRSoftware.InnoSetup` |

Visual Studio is not required. If you use it, install the *.NET desktop development* workload.

## 1. Get the converter programs

Mercury runs FFmpeg, ImageMagick and libjxl as separate programs. They are large, so they are not in the repository. One script downloads and checks them:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\fetch-tools.ps1
```

They are placed in `tools\`. The script checks each download against the checksum its publisher lists.

## 2. Build and test

```powershell
dotnet build MercuryConverter.slnx
dotnet test MercuryConverter.slnx
```

Tests that run real conversions skip themselves if `tools\` is missing, so the rest still run.

## 3. Run the app

```powershell
src\Mercury.Converter.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\MercuryConverter.exe
```

Command-line switches the app understands:

| Switch | What it does |
|---|---|
| `--convert <format> [--quality N] <file>` | Opens the app (or the small window) and converts the file. This is what the right-click menu runs. |
| `--quick <file>` | Opens the small window with its format list. |
| `--install-menu` / `--remove-menu` | Adds or removes the right-click menu without opening a window. |
| `<file> <file> ...` | Opens the main window with these files added. |

## 4. Make the installer and release files

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
powershell -ExecutionPolicy Bypass -File scripts\make-release-assets.ps1
```

The first script publishes a self-contained build and compiles the installer into `dist\`. The second makes the portable zip and copies both files into `dist\release\` with names that have no version number, plus `SHA256SUMS.txt`. Those are the files to attach to a GitHub release.

## Project layout

| Folder | What is in it |
|---|---|
| `src/Mercury.Converter.Core` | The conversion engine, with no user interface: command building, quality mapping, safe output, the resource governor, thumbnails, presets, the format catalog |
| `src/Mercury.Converter.App` | The WinUI 3 app: windows, view models, styles |
| `tests/Mercury.Converter.Core.Tests` | Unit tests and real conversions |
| `installer/` | Inno Setup script |
| `scripts/` | Tool fetching, installer and release build |
| `branding/` | Icon and logo files |
| `docs/` | This documentation site, design files and the UI study |

## How a conversion works

1. The app turns your choices into a `ConversionRequest`.
2. `CommandBuilder` turns the request into one or more command lines for FFmpeg, ImageMagick or libjxl. Arguments are passed as a list, never glued into a string, so file names cannot inject anything.
3. `ConversionRunner` runs them, writes to a temporary file, checks the size, and renames the file only when it is complete.
4. `ConversionScheduler` decides how many jobs run at once, after asking `ResourceGovernor` how busy the PC is.

## Design

The look of the app follows the design in [`docs/design`](design). Colors, sizes and wording are in [`design/HANDOFF.md`](design/HANDOFF.md). The XAML styles that carry them are in `src/Mercury.Converter.App/Styles`.

## Contributing

Issues and pull requests are welcome. Please run `dotnet test` before sending a change, and keep user-facing wording short and plain: the app is for people who do not think about codecs.
