# Mercury Converter 1.0.0: test checklist

## Files to test
- `dist\MercuryConverter-1.0.0-x64-setup.exe`: installer. Choose "just for me" to avoid needing administrator rights.
- `dist\MercuryConverter-1.0.0-portable-x64.zip`: unzip anywhere and run `MercuryConverter.exe`.
- Both files have a `.sha256` file next to them. Check the hash before running.

## Checks that have passed
- 143 automated tests (`dotnet test tests\Mercury.Converter.Core.Tests`), including real conversions to every format.
- Install, add the right-click menu, convert a video with "(low quality)", and uninstall. Nothing was left behind.
- The window with previews at every size, and with previews off, with live progress and results on each file.
- Registry entries for the right-click menu match the File Converter menu you showed, in the same order.

## Please check by hand
1. **Right-click menu.** Turn on "Show in right-click menu", then right-click a video in File Explorer.
   On Windows 11 choose "Show more options" (or hold Shift while you right-click). You should see
   "Mercury Converter" with: To Mkv, To Mp4, To Mp4 (low quality), To Webm, To Ogv, To Avi, To Gif, To Gif (low quality),
   To Ogg, To Wav, To Mp3, To Aac.
2. **Several files at once.** Select 3 or 4 files, right-click, and pick one format. They should open in one window.
3. **Progress.** Add a long video and several photos. Waiting files show the moving bar. When Convert starts, each
   file's bar fills with a percentage in the same place.
4. **Previews.** Use View to change preview size, or turn previews off.
5. **Speed.** Try Smart, Gentle, and Maximum speed with a large video while you do something else, and see how the
   computer feels. Try "Use my graphics card to encode video": it should use much less processor, and make larger files.
6. **Size guard.** Convert a video that is already small. The app should lower the quality to avoid a bigger file.
7. **Presets, resize, rotate, flip** as in the previous checklist.

## Known limits
- Not code-signed, so Windows SmartScreen may warn about the installer.
- Messages shown after a conversion are in English. Spanish, French, German and Brazilian Portuguese window text
  are draft translations.
- The right-click menu is the classic menu. A top-level entry in the new Windows 11 menu needs a separate packaged
  extension, which is not built.
- Graphics-card encoding makes files about 60% larger in testing. It is off by default.
- If a file cannot be made smaller even at the lowest quality tried, the smallest result is kept and a note is shown.
