# Signs files with Authenticode, so Windows can show who published them.
#
# Pick one way to sign by setting environment variables (nothing secret is ever written to the repository):
#
#   A certificate in the Windows certificate store (a hardware token, for example):
#       MERCURY_SIGN_THUMBPRINT        the certificate's SHA-1 thumbprint
#
#   A .pfx file:
#       MERCURY_SIGN_PFX               path to the .pfx
#       MERCURY_SIGN_PFX_PASSWORD      its password
#
#   Azure Artifact Signing (formerly Trusted Signing):
#       MERCURY_SIGN_AZURE_DLIB        path to Azure.CodeSigning.Dlib.dll
#       MERCURY_SIGN_AZURE_METADATA    path to the metadata .json (endpoint, account, profile)
#
# If none is set, nothing is signed and the script says so. Add -Required to make that an error.
# SignPath is used from GitHub Actions instead (see .github\workflows\release.yml and docs\SIGNING.md).

[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$Files,
    [string]$TimestampUrl = "http://timestamp.acs.microsoft.com",
    [switch]$Required
)

$ErrorActionPreference = "Stop"

if (-not $Files -or $Files.Count -eq 0) { throw "Give the files to sign, for example: sign.ps1 MercuryConverter.exe" }

function Find-SignTool {
    $found = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($found) { return $found.Source }
    # The Windows SDK has it, and so does the NuGet package the app is built with.
    $places = @(
        (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"),
        (Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools")
    )
    $candidate = $places | Where-Object { Test-Path $_ } | ForEach-Object {
            Get-ChildItem $_ -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue
        } |
        Where-Object { $_.FullName -match "\\x64\\" } |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($candidate) { return $candidate.FullName }
    return $null
}

$thumbprint = $env:MERCURY_SIGN_THUMBPRINT
$pfx = $env:MERCURY_SIGN_PFX
$dlib = $env:MERCURY_SIGN_AZURE_DLIB

if (-not ($thumbprint -or $pfx -or $dlib)) {
    $message = "Signing is not set up (see scripts\sign.ps1). The files are left unsigned."
    if ($Required) { throw $message }
    Write-Host $message
    return
}

$signtool = Find-SignTool
if (-not $signtool) { throw "signtool.exe was not found. Install the Windows SDK." }

$arguments = @("sign", "/fd", "SHA256", "/td", "SHA256", "/tr", $TimestampUrl, "/d", "Mercury Converter", "/du", "https://github.com/AfridexD/MercuryConverter")

if ($dlib) {
    $arguments += @("/dlib", $dlib, "/dmdf", $env:MERCURY_SIGN_AZURE_METADATA)
}
elseif ($pfx) {
    $arguments += @("/f", $pfx)
    if ($env:MERCURY_SIGN_PFX_PASSWORD) { $arguments += @("/p", $env:MERCURY_SIGN_PFX_PASSWORD) }
}
else {
    $arguments += @("/sha1", $thumbprint)
}

foreach ($file in $Files) {
    if (-not (Test-Path $file)) { throw "Cannot sign '$file': it does not exist." }
    Write-Host "Signing $file"
    & $signtool @arguments $file
    if ($LASTEXITCODE -ne 0) { throw "signtool failed for '$file'." }

    $signature = Get-AuthenticodeSignature $file
    if (-not $signature.SignerCertificate) { throw "'$file' has no signature after signing." }
}
