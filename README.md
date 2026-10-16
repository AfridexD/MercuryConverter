# Mercury Converter

A free, open-source file converter for Windows. Convert photos, animated GIFs, video, and audio on your own computer, from a simple window or from File Explorer's right-click menu. Nothing is uploaded.

> **Status:** early version (1.0). It works and is tested, but it is not code-signed yet, so Windows SmartScreen may warn you the first time you run the installer.

## Features

- **Many formats**
  - Images: JPEG, PNG, WebP, AVIF, JPEG XL, TIFF, PDF
  - Animation: GIF
  - Video: MP4, MKV, WebM, OGV, AVI
  - Audio: MP3, FLAC, OGG, WAV, AAC
- **One quality slider** from *Smallest file* to *Best quality*, instead of codec settings.
- **Files never get bigger by surprise.** If a result would be larger than the original, the app lowers the quality and tries again.
- **Lossless JPEG to JPEG XL**, which can be turned back into the exact original JPEG.
- **Resize, rotate, and flip**, plus presets you can save, import, and export.
- **Previews** of every file, in three sizes or off.
- **Right-click menu** in File Explorer: *To Mp4*, *To Webp*, and so on.
- **Uses your PC sensibly.** It checks how busy the computer is and adjusts, and can decode video on the graphics card. Encoding on the graphics card is optional (it uses less processor but makes larger files).
- **Safe output.** Files are written under a temporary name and renamed only when finished. Existing files are never overwritten.
- **Progress** for every file, and on the taskbar.

## Requirements

Windows 10 version 2004 (build 19041) or later, 64-bit. Windows 11 is fine.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
# 1. Fetch the converter programs (FFmpeg, libjxl, ImageMagick) into .\tools
#    Needs 7-Zip and ImageMagick installed: winget install 7zip.7zip ImageMagick.ImageMagick
powershell -ExecutionPolicy Bypass -File scripts\fetch-tools.ps1

# 2. Build and test
dotnet build MercuryConverter.slnx
dotnet test MercuryConverter.slnx
```

The tests that run real conversions are skipped automatically if `tools\` is missing, so the rest of the tests still run.

To run the app after building:

```powershell
src\Mercury.Converter.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\MercuryConverter.exe
```

### Build the installer

Needs [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

This writes the installer to `dist\` with a SHA-256 checksum. The installer asks whether to install for just you or for all users. Uninstalling removes the right-click menu entries. Your settings and presets in `%LOCALAPPDATA%\MercuryConverter` are kept.

## Project layout

| Folder | What is in it |
|---|---|
| `src/Mercury.Converter.Core` | Conversion engine: commands, quality mapping, safe output, resource governor, thumbnails, presets |
| `src/Mercury.Converter.App` | The WinUI 3 app |
| `tests/` | Unit tests and real-conversion tests |
| `scripts/` | Tool fetching, installer build, translation files |
| `installer/` | Inno Setup script |
| `docs/` | UI/UX study and the redesign brief |

## Translations

Window text lives in `scripts/strings.json`. Run `node scripts/build-strings.js` after editing it to write one `Resources.resw` per language. Spanish, French, German, and Brazilian Portuguese are machine-written drafts and need review by a native speaker. Messages shown after a conversion are still English only.

## Known limits

- The right-click menu is in the classic Windows menu. On Windows 11, choose **Show more options** (or hold Shift while right-clicking).
- Not code-signed yet.
- Light theme, high contrast, and right-to-left layouts have not been tested. See [docs/UIUX-STUDY.md](docs/UIUX-STUDY.md) for the full list of known interface issues and the plan.

## License

Mercury Converter is MIT licensed. See [LICENSE](LICENSE).

The downloads also include FFmpeg (GPL v3), ImageMagick, and libjxl, which have their own licenses. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
