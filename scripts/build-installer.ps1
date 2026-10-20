# Builds the Mercury Converter installer.
# 1. Publishes a self-contained x64 Release build to publish\win-x64.
# 2. Compiles installer\MercuryConverter.iss with Inno Setup 6 into dist\.
# 3. Writes a SHA-256 checksum next to the installer.
#
# Requires: .NET 10 SDK, Inno Setup 6 (https://jrsoftware.org/isinfo.php), and the tools folder
# (see README.md for the converter programs).
#
# -Sign          Signs the app's own files, the installer and the uninstaller (see scripts\sign.ps1 for how to set up
#                a certificate). Without a certificate it just says so and carries on.
# -SkipPublish   Uses what is already in publish\win-x64. GitHub Actions uses this to build the installer from files
#                that were signed in an earlier step.

[CmdletBinding()]
param(
    [string]$InnoCompiler = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    [switch]$Sign,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root "src\Mercury.Converter.App"
$publish = Join-Path $root "publish\win-x64"
$signScript = Join-Path $PSScriptRoot "sign.ps1"

foreach ($tool in @("ffmpeg\ffmpeg.exe", "libjxl\cjxl.exe", "libjxl\djxl.exe", "imagemagick\magick.exe")) {
    if (-not (Test-Path (Join-Path $root "tools\$tool"))) {
        throw "Missing tools\$tool. Add the converter programs first (see README.md)."
    }
}

if (-not (Test-Path $InnoCompiler)) {
    throw "Inno Setup not found at '$InnoCompiler'. Install it, or pass -InnoCompiler with the path to ISCC.exe."
}

if (-not $SkipPublish) {
    Write-Host "Publishing the app..."
    dotnet publish "$app\Mercury.Converter.App.csproj" -c Release -r win-x64 -p:Platform=x64 -p:PublishDir="$publish\" --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

    if ($Sign) {
        # Only Mercury's own files are signed. The converter programs in tools\ keep their makers' signatures, or none.
        Write-Host "Signing the app..."
        $own = @("MercuryConverter.exe", "MercuryConverter.dll", "Mercury.Converter.Core.dll") | ForEach-Object { Join-Path $publish $_ }
        & $signScript @own
    }
}

Write-Host "Building the installer..."
$innoArguments = @()
if ($Sign) {
    # Inno signs the installer and the uninstaller by calling sign.ps1. $f stands for the file, and is filled in by Inno.
    $command = "powershell -NoProfile -ExecutionPolicy Bypass -File `"$signScript`" `$f"
    $innoArguments += @("/DSign=1", "/Smercurysign=$command")
}
& $InnoCompiler @innoArguments (Join-Path $root "installer\MercuryConverter.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

$installer = Get-ChildItem (Join-Path $root "dist") -Filter "MercuryConverter-*-setup.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$hash = (Get-FileHash -Path $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $($installer.Name)" | Set-Content -Path "$($installer.FullName).sha256" -Encoding ascii

Write-Host "Done: $($installer.FullName)"
Write-Host "SHA-256: $hash"
