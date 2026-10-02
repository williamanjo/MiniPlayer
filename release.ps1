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
    "--mainExe", "MiniPlayer.exe", "--packTitle", "MiniPlayer", "--packAuthors", "williamanjo",
    "--icon", "Assets\app.ico", "--outputDir", $OutDir
)
if ($Notes) { $pack += @("--releaseNotes", $Notes) }
vpk @pack
if ($LASTEXITCODE) { throw "vpk pack failed" }

if ($Upload) {
    vpk upload github --repoUrl $repo --token (gh auth token) --publish `
        --releaseName "MiniPlayer v$Version" --tag "v$Version" --outputDir $OutDir
    if ($LASTEXITCODE) { throw "vpk upload failed" }
    if ($Notes) { gh release edit "v$Version" --notes-file $Notes }
}

Get-ChildItem $OutDir -Filter "*$Version*" | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
