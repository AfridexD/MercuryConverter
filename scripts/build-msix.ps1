# Builds the Microsoft Store package (.msix) of Mercury Converter.
# 1. Publishes a self-contained x64 Release build to publish\msix. The resource index of this build is named after the
#    package identity, which Windows requires for a packaged app.
# 2. Makes the right-click menu lines from the app's own list, and fills in installer\msix\AppxManifest.template.xml.
# 3. Packs everything into dist\MercuryConverter-<version>-x64.msix.
#
# The package is NOT signed. Partner Center signs it for you when you submit it to the Store.
#
# For the Store, pass the three "Product identity" values that Partner Center shows for your reserved app name:
#   .\scripts\build-msix.ps1 -ForStore -IdentityName "12345Name.MercuryConverter" -Publisher "CN=XXXXXXXX-XXXX-..." -PublisherDisplayName "Your name"
# Without them a package with test values is built. It is only good for checking that packing works.
#
# Requires: .NET 10 SDK, the tools folder (see README.md), and makeappx.exe from the Windows SDK
# (the Microsoft.Windows.SDK.BuildTools NuGet package that the app already restores contains it).

[CmdletBinding()]
param(
    [string]$IdentityName = "MercuryConverter.LocalTest",
    [string]$Publisher = "CN=MercuryConverterLocalTest",
    [string]$PublisherDisplayName = "Mercury Converter",
    # Must be one of the names reserved in Partner Center. The name people see in Start stays "Mercury Converter".
    [string]$PackageDisplayName = "Mercury Converter",
    [string]$Version,
    [string]$MakeAppx,
    [switch]$ForStore,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root "src\Mercury.Converter.App"
$stage = Join-Path $root "publish\msix"
$dist = Join-Path $root "dist"
$template = Join-Path $root "installer\msix\AppxManifest.template.xml"

if ($ForStore -and ($IdentityName -eq "MercuryConverter.LocalTest" -or $Publisher -eq "CN=MercuryConverterLocalTest")) {
    throw "For the Store, pass -IdentityName and -Publisher exactly as Partner Center shows them under Product identity."
}

foreach ($tool in @("ffmpeg\ffmpeg.exe", "libjxl\cjxl.exe", "libjxl\djxl.exe", "imagemagick\magick.exe")) {
    if (-not (Test-Path (Join-Path $root "tools\$tool"))) {
        throw "Missing tools\$tool. Add the converter programs first (see README.md)."
    }
}

if (-not $Version) {
    $csproj = [xml](Get-Content (Join-Path $app "Mercury.Converter.App.csproj"))
    $three = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    # The Store needs the last number to be 0.
    $Version = "$three.0"
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw "The package version must look like 1.0.8.0 (the Store needs the last number to be 0). Got '$Version'."
}

if (-not $MakeAppx) {
    $found = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $found) {
        $found = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    }
    if (-not $found) { throw "makeappx.exe not found. Install the Windows SDK or pass -MakeAppx with its path." }
    $MakeAppx = $found.FullName
}

if (-not $SkipPublish) {
    Write-Host "Publishing the app for the package..."
    if (Test-Path $stage) { [IO.Directory]::Delete($stage, $true) }
    # ProjectPriIndexName names the app's resource index after the package. ProjectPriFileName is the file name Windows
    # looks for in a package.
    dotnet publish "$app\Mercury.Converter.App.csproj" -c Release -r win-x64 -p:Platform=x64 -p:PublishDir="$stage\" `
        "-p:ProjectPriIndexName=$IdentityName" "-p:ProjectPriFileName=resources.pri" --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}

if (-not (Test-Path (Join-Path $stage "resources.pri"))) {
    throw "publish\msix\resources.pri is missing. Run without -SkipPublish."
}

Write-Host "Writing the right-click menu lines..."
$menuFile = Join-Path $dist "store-menu.xml"
New-Item -ItemType Directory -Force $dist | Out-Null
if (Test-Path $menuFile) { Remove-Item $menuFile }
Start-Process (Join-Path $stage "MercuryConverter.exe") -ArgumentList "--write-store-menu", "`"$menuFile`"" -Wait
if (-not (Test-Path $menuFile)) { throw "The app did not write the menu lines." }
$menu = Get-Content $menuFile -Raw
Remove-Item $menuFile

$manifest = Get-Content $template -Raw
$escape = { param($text) [System.Security.SecurityElement]::Escape($text) }
$manifest = $manifest.Replace("{{IdentityName}}", (& $escape $IdentityName)).Replace("{{Publisher}}", (& $escape $Publisher))
$manifest = $manifest.Replace("{{PublisherDisplayName}}", (& $escape $PublisherDisplayName)).Replace("{{PackageDisplayName}}", (& $escape $PackageDisplayName))
$manifest = $manifest.Replace("{{Version}}", $Version).Replace("{{MenuExtensions}}", $menu)
Set-Content (Join-Path $stage "AppxManifest.xml") $manifest -Encoding utf8

$output = Join-Path $dist "MercuryConverter-$($Version.Substring(0, $Version.LastIndexOf('.')))-x64.msix"
if (Test-Path $output) { Remove-Item $output }
Write-Host "Packing..."
& $MakeAppx pack /d $stage /p $output /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed. Read its message above." }

$hash = (Get-FileHash -Path $output -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ""
Write-Host "Done: $output"
Write-Host ("Size: {0:n0} MB   SHA-256: {1}" -f ((Get-Item $output).Length / 1MB), $hash)
if (-not $ForStore) {
    Write-Host "This used test identity values. It cannot be uploaded to the Store. See docs\MICROSOFT-STORE.md."
}
