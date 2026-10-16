# Downloads the converter programs Mercury Converter runs, into the "tools" folder.
#
#   tools\ffmpeg\       FFmpeg (BtbN GPL shared build)
#   tools\libjxl\       cjxl.exe and djxl.exe (JPEG XL reference tools)
#   tools\imagemagick\  ImageMagick 7 (copied from an installed copy)
#
# FFmpeg and libjxl are downloaded from GitHub, and each file is checked against the SHA-256 digest that
# GitHub publishes for the release asset. A file that does not match is deleted and the script stops.
#
# Requires: PowerShell 5.1 or later, 7-Zip (for the libjxl archive), and ImageMagick installed
# (winget install ImageMagick.ImageMagick).

[CmdletBinding()]
param(
    [string]$ToolsRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) "tools"),
    [string]$LibjxlVersion = "v0.12.0",
    [string]$SevenZip = "C:\Program Files\7-Zip\7z.exe"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Get-VerifiedDownload {
    param([string]$Repo, [string]$Tag, [string]$AssetName, [string]$Destination)

    $release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/tags/$Tag" -Headers @{ "User-Agent" = "mercury-converter-setup" }
    $asset = $release.assets | Where-Object name -eq $AssetName
    if (-not $asset) { throw "Release $Tag of $Repo has no file named $AssetName." }
    if (-not $asset.digest -or -not $asset.digest.StartsWith("sha256:")) { throw "GitHub did not publish a SHA-256 digest for $AssetName." }

    Write-Host "Downloading $AssetName ($([math]::Round($asset.size / 1MB)) MB)..."
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $Destination

    $expected = $asset.digest.Substring("sha256:".Length)
    $actual = (Get-FileHash -Path $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        Remove-Item $Destination -Force
        throw "Checksum mismatch for $AssetName. Expected $expected but got $actual. The file was deleted."
    }

    Write-Host "  checksum OK ($expected)"
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ("mercury-tools-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    # --- libjxl -------------------------------------------------------------------------------------
    if (-not (Test-Path $SevenZip)) { throw "7-Zip was not found at '$SevenZip'. Install it (winget install 7zip.7zip) or pass -SevenZip." }
    $jxlArchive = Join-Path $temp "jxl.7z"
    Get-VerifiedDownload -Repo "libjxl/libjxl" -Tag $LibjxlVersion -AssetName "jxl-x64-windows-static.7z" -Destination $jxlArchive
    & $SevenZip x $jxlArchive "-o$temp\jxl" -y | Out-Null
    $jxlDir = Join-Path $ToolsRoot "libjxl"
    New-Item -ItemType Directory -Force $jxlDir | Out-Null
    foreach ($exe in "cjxl.exe", "djxl.exe") {
        Copy-Item (Get-ChildItem "$temp\jxl" -Recurse -Filter $exe | Select-Object -First 1).FullName $jxlDir -Force
    }

    # --- FFmpeg -------------------------------------------------------------------------------------
    $ffmpegArchive = Join-Path $temp "ffmpeg.zip"
    Get-VerifiedDownload -Repo "BtbN/FFmpeg-Builds" -Tag "latest" -AssetName "ffmpeg-master-latest-win64-gpl-shared.zip" -Destination $ffmpegArchive
    Expand-Archive -Path $ffmpegArchive -DestinationPath "$temp\ffmpeg" -Force
    $ffmpegRoot = Get-ChildItem "$temp\ffmpeg" -Directory | Select-Object -First 1
    $ffmpegDir = Join-Path $ToolsRoot "ffmpeg"
    New-Item -ItemType Directory -Force $ffmpegDir | Out-Null
    Copy-Item (Join-Path $ffmpegRoot.FullName "bin\ffmpeg.exe") $ffmpegDir -Force
    Copy-Item (Join-Path $ffmpegRoot.FullName "bin\*.dll") $ffmpegDir -Force
    Get-ChildItem $ffmpegRoot.FullName -Filter "LICENSE*" | Copy-Item -Destination $ffmpegDir -Force

    # --- ImageMagick --------------------------------------------------------------------------------
    $magickDir = Join-Path $ToolsRoot "imagemagick"
    $installed = Get-ChildItem "C:\Program Files" -Directory -Filter "ImageMagick-7*" -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $installed) { throw "ImageMagick 7 is not installed. Run: winget install ImageMagick.ImageMagick   then run this script again." }
    if (Test-Path $magickDir) { Remove-Item $magickDir -Recurse -Force }
    Copy-Item $installed.FullName $magickDir -Recurse -Force
    Write-Host "Copied ImageMagick from $($installed.FullName)"

    Write-Host ""
    Write-Host "Done. Tools are in $ToolsRoot"
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
