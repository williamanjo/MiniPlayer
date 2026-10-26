<#
.SYNOPSIS
  Builds the MiniPlayer installer (Velopack) and optionally publishes it to GitHub Releases.

.EXAMPLE
  ./release.ps1 -Version 1.1.0                    # local build into ./releases
  ./release.ps1 -Version 1.1.0 -Notes notes.md -Upload   # build + publish release v1.1.0

.NOTES
  Requires: .NET SDK, vpk (dotnet tool install -g vpk), gh (logged in) for -Upload.
  Installed copies check this repo's releases and offer the update.
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Notes,
    [switch]$Upload,
    [string]$OutDir = "releases"
)

$ErrorActionPreference = "Stop"
$repo = "https://github.com/williamanjo/MiniPlayer"
Set-Location $PSScriptRoot

if ($Upload) {
    # Previous release lets vpk build a small delta package.
    vpk download github --repoUrl $repo --outputDir $OutDir
}

Remove-Item -Recurse -Force publish -ErrorAction SilentlyContinue
dotnet publish -c Release "-p:Version=$Version" -o publish
if ($LASTEXITCODE) { throw "dotnet publish failed" }

$pack = @(
    "pack", "--packId", "MiniPlayer", "--packVersion", $Version, "--packDir", "publish",
    "--mainExe", "MiniPlayer.exe", "--packTitle", "MiniPlayer for Browser Music", "--packAuthors", "williamanjo",
    "--icon", "Assets\app.ico", "--outputDir", $OutDir,
    # Setup.exe shows the hero art with a progress bar while it installs
    "--splashImage", "Assets\installer-splash.png", "--splashProgressColor", "#F472B6"
)
if ($Notes) { $pack += @("--releaseNotes", $Notes) }
vpk @pack
if ($LASTEXITCODE) { throw "vpk pack failed" }

# Our installer (choices, progress, theme) carries Velopack's Setup.exe inside and runs it silently;
# it replaces Velopack's Setup.exe as the download. Updates still come from the Velopack packages.
$installerOut = Join-Path $OutDir "installer"
$velopackSetup = (Resolve-Path (Join-Path $OutDir "MiniPlayer-win-Setup.exe")).Path
dotnet build Installer\MiniPlayerSetup.csproj -c Release "-p:Version=$Version" "-p:VelopackSetup=$velopackSetup" -o $installerOut
if ($LASTEXITCODE) { throw "installer build failed" }
$installer = Join-Path $installerOut "MiniPlayer-win-Setup.exe"

if ($Upload) {
    vpk upload github --repoUrl $repo --token (gh auth token) --publish `
        --releaseName "MiniPlayer for Browser Music v$Version" --tag "v$Version" --outputDir $OutDir
    if ($LASTEXITCODE) { throw "vpk upload failed" }
    gh release upload "v$Version" $installer --clobber
    if ($LASTEXITCODE) { throw "installer upload failed" }
    if ($Notes) { gh release edit "v$Version" --notes-file $Notes }
}

Get-Item $installer | Select-Object @{ n = "Name"; e = { "installer\$($_.Name)" } }, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }

Get-ChildItem $OutDir -Filter "*$Version*" | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
