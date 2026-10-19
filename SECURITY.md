# Security

## Reporting a problem

If you find a security problem in Mercury Converter, please [open a private security advisory](https://github.com/AfridexD/MercuryConverter/security/advisories/new) on GitHub instead of a public issue. Say what you found and how to repeat it, and we will answer as soon as we can.

## How Mercury protects you

- **Nothing leaves your computer.** Files are converted locally. The only network request the app can make is the optional update check, which is off until you turn it on. It asks GitHub for the newest version number and sends nothing else.
- **No installing for you.** The update notice only opens the release page. Mercury never downloads or runs an update by itself.
- **Your originals are never changed**, and existing files are never overwritten.
- **Converter programs run with limits.**
  - They are started with an argument list, never a command string, so a file name cannot add commands.
  - FFmpeg may only read local files and pipes, so a crafted file cannot make it fetch web addresses.
  - ImageMagick uses Mercury's own security policy (`installer/magick-policy.xml`), which switches off network access, scripts, file lists and Ghostscript formats, and caps memory and disk use.
- **Only your account is changed.** The right-click menu is written to your own user settings. No administrator rights are needed for "just me" installs.
- **Safe loading.** The app loads system libraries only from the Windows folder, preset files are size-limited and checked before use, and the "open this file" pipe between copies of the app accepts only the current user.

## Checking your download

Every release has a `SHA256SUMS.txt`. In PowerShell: `Get-FileHash .\MercuryConverter-x64-setup.exe` and compare the result. The installer is not code-signed yet, so Windows SmartScreen may warn the first time.
