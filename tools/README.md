# tools

This folder holds the converter programs the app runs. They are large downloads, so they are not stored in git.

Run this from the repository root to download them (checksums are verified):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\fetch-tools.ps1
```

It creates `ffmpeg\`, `libjxl\`, and `imagemagick\` here. See `THIRD-PARTY-NOTICES.md` for their licenses.
