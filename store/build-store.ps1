<#
.SYNOPSIS
  Builds the Microsoft Store package (MSIX) of MiniPlayer.

.DESCRIPTION
  The Store signs the package itself, so the .msix uploaded to Partner Center can stay unsigned.
  Identity values: Partner Center → your app → Product management → Product identity.

.EXAMPLE
  ./store/build-store.ps1 -Version 1.6.0 `
      -IdentityName "12345YourName.MiniPlayer" -Publisher "CN=ABCD1234-..." -PublisherDisplayName "Your Name"
#>
param(
    [Parameter(Mandatory)][string]$Version,
    # Partner Center → Product management → Product identity (Store ID 9P1LQ9M6QTJ9).
    [string]$IdentityName = "williamanjo.MiniPlayerforBrowserMusic",
    [string]$Publisher = "CN=59D64183-1875-4A28-8BE2-F4FA5BB51D83",
    [string]$PublisherDisplayName = "williamanjo",
    [string]$DisplayName = "MiniPlayer for Browser Music"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$out = Join-Path $PSScriptRoot "out"
$staging = Join-Path $out "package"

# Windows SDK tools (MakeAppx, MakePri): newest installed x64 version.
$sdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName "x64\makeappx.exe") } |
    Sort-Object { [version]$_.Name } | Select-Object -Last 1
if (-not $sdk) { throw "Windows SDK not found (MakeAppx.exe). Install the Windows 10/11 SDK." }
$makeappx = Join-Path $sdk.FullName "x64\makeappx.exe"
$makepri = Join-Path $sdk.FullName "x64\makepri.exe"

# Store versions have 4 parts and the last one must be 0.
$msixVersion = "$Version.0"

Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $staging | Out-Null

Write-Host "1/4 publish (Store flavor)"
dotnet publish (Join-Path $root "MiniPlayer.csproj") -c Release -p:StoreBuild=true "-p:Version=$Version" -o $staging -nologo
if ($LASTEXITCODE) { throw "dotnet publish failed" }

Write-Host "2/4 assets + manifest"
& (Join-Path $PSScriptRoot "make-assets.ps1") -OutDir (Join-Path $staging "Assets") | Out-Null
(Get-Content (Join-Path $PSScriptRoot "AppxManifest.template.xml") -Raw) `
    -replace '\$IdentityName\$', $IdentityName `
    -replace '\$Publisher\$', $Publisher `
    -replace '\$PublisherDisplayName\$', $PublisherDisplayName `
    -replace '\$DisplayName\$', $DisplayName `
    -replace '\$Version\$', $msixVersion |
    Set-Content (Join-Path $staging "AppxManifest.xml") -Encoding utf8

Write-Host "3/4 resources.pri (scale/targetsize variants of the assets)"
$priConfig = Join-Path $out "priconfig.xml"
& $makepri createconfig /cf $priConfig /dq pt-BR /pv 10.0.0 /o | Out-Null
& $makepri new /pr $staging /cf $priConfig /of (Join-Path $staging "resources.pri") /o | Out-Null
if ($LASTEXITCODE) { throw "makepri failed" }

Write-Host "4/4 pack"
$msix = Join-Path $out "MiniPlayer_$($msixVersion)_x64.msix"
& $makeappx pack /d $staging /p $msix /o | Out-Null
if ($LASTEXITCODE) { throw "makeappx failed" }

Get-Item $msix | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
