# Code signing

Code signing puts the publisher's name on the installer and the program, so Windows can show who made them. It is the biggest step towards getting rid of the blue "Windows protected your PC" warning (SmartScreen).

**What to expect.** Signing does not remove the warning on day one. SmartScreen also looks at how many people have downloaded and run a file. A signed file from a new publisher can still show the warning for the first weeks, and it fades as downloads grow. Unsigned files do not show a publisher name at all, and are warned about for much longer.

## What is already built

Everything on our side is ready, so once a certificate exists, signing is a matter of switching it on:

| Piece | What it does |
|---|---|
| `scripts/sign.ps1` | Signs files with `signtool`, with a timestamp so the signature stays valid after the certificate expires. |
| `scripts/build-installer.ps1 -Sign` | Signs Mercury's own program files, then the installer and the uninstaller. The converter programs (FFmpeg and the others) keep their makers' signatures. |
| `.github/workflows/release.yml` | Builds, tests and packs a release on GitHub from source, signs through SignPath when configured, and makes a *draft* release to review. |
| `.signpath/artifact-configurations/` | The two SignPath configurations (`app` and `installer`) to paste into SignPath. |
| [Code signing policy](https://afridexd.github.io/MercuryConverter/code-signing.html) | The policy page that SignPath asks for. |

The local signing was tested with a throwaway certificate: the program files, the installer and the uninstaller all carried the signature and a Microsoft timestamp. The SignPath steps in the workflow cannot be tested until the SignPath account exists.

## Choose a certificate

| Option | Cost | Good for | Notes |
|---|---|---|---|
| **SignPath Foundation** | Free for open source | This project | You apply, they review the project, and signing runs from GitHub Actions. The certificate is theirs, so the publisher name reads "SignPath Foundation". |
| **Azure Artifact Signing** (was Trusted Signing) | About 10 US dollars a month | Your own name or company on the file | Needs identity checks, and availability depends on country. Check the current rules before paying. |
| **Certificate authority (OV or EV)** | About 200 to 500 US dollars a year | Full control | Comes on a hardware token or a cloud service. No longer gives instant SmartScreen trust, even EV. |

## Option 1: SignPath Foundation (recommended first step)

1. Read the requirements and apply at <https://signpath.org/foundation/apply>. Mercury fits the usual rules: MIT license, public repository, no ads or tracking, built from source in CI.
2. They may ask about the third-party programs inside the installer (FFmpeg, ImageMagick, libjxl). Point them to `THIRD-PARTY-NOTICES.md`. Only Mercury's own files are signed.
3. When approved, in SignPath create a project with the slug `MercuryConverter`, a signing policy `release-signing`, and the two artifact configurations from `.signpath/artifact-configurations/` (`app` and `installer`).
4. In this repository's settings add:
   - a secret `SIGNPATH_API_TOKEN` (from SignPath),
   - a variable `SIGNPATH_ORGANIZATION_ID` (also from SignPath).
5. Push a tag such as `v1.0.2`. The workflow builds, signs the program files, builds the installer from them, signs the installer, and creates a **draft** release. Check the signatures in the "Check signatures" step, add release notes, and publish.
6. Update the [code signing policy](https://afridexd.github.io/MercuryConverter/code-signing.html) page: change the status line, and add the wording SignPath requires.

## Option 2: your own certificate, on your PC

Set one of these before running the build (nothing secret goes in the repository):

```powershell
# A certificate in the Windows store (for example on a hardware token)
$env:MERCURY_SIGN_THUMBPRINT = "<thumbprint>"

# or a .pfx file
$env:MERCURY_SIGN_PFX = "C:\keys\mercury.pfx"
$env:MERCURY_SIGN_PFX_PASSWORD = "<password>"

# or Azure Artifact Signing
$env:MERCURY_SIGN_AZURE_DLIB = "C:\tools\Azure.CodeSigning.Dlib.dll"
$env:MERCURY_SIGN_AZURE_METADATA = "C:\tools\metadata.json"

.\scripts\build-installer.ps1 -Sign
.\scripts\make-release-assets.ps1
```

Then check the result: `Get-AuthenticodeSignature .\dist\release\MercuryConverter-x64-setup.exe` should say `Valid` and show your name.

## After signing

- Publish the signed checksums with the release (`make-release-assets.ps1` does this).
- Change the SmartScreen advice in the README, the FAQ and the docs home page. Keep the "More info, then Run anyway" tip for a while: reputation takes time.
- Never sign builds that did not come from the repository's CI or your own clean build, and never share the certificate or token.
