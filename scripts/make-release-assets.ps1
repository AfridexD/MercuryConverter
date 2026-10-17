# Prepares the files that go on a GitHub release, in dist\release.
# Run scripts\build-installer.ps1 first: it publishes the app and builds the installer.
#
# The files have no version number in their names, so these links always point at the newest release:
#   https://github.com/AfridexD/MercuryConverter/releases/latest/download/MercuryConverter-x64-setup.exe
#   https://github.com/AfridexD/MercuryConverter/releases/latest/download/MercuryConverter-x64-portable.zip

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "publish\win-x64"
$out = Join-Path $root "dist\release"

$installer = Get-ChildItem (Join-Path $root "dist") -Filter "MercuryConverter-*-setup.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $installer) { throw "No installer in dist. Run scripts\build-installer.ps1 first." }

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $out | Out-Null

Copy-Item $installer.FullName (Join-Path $out "MercuryConverter-x64-setup.exe")

Write-Host "Zipping the portable version..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $out "MercuryConverter-x64-portable.zip"
[System.IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

$lines = foreach ($file in Get-ChildItem $out -File) {
    "$((Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($file.Name)"
}
$lines | Set-Content (Join-Path $out "SHA256SUMS.txt") -Encoding ascii

Write-Host "Release files are in $out"
$lines
