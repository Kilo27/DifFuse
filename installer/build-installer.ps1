<#
.SYNOPSIS
Builds the DifFuse installer: artifacts\installer\DifFuse-Setup-<version>.exe.

.DESCRIPTION
Runs the tests, publishes a self-contained win-x64 build of DifFuse, and packs it into a
one-click setup with Inno Setup. Needs the .NET 10 SDK and Inno Setup 6
(winget install --id JRSoftware.InnoSetup -e).

The version comes from <Version> in Directory.Build.props.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
#>
[CmdletBinding()]
param(
    # Skip "dotnet test" before publishing.
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'artifacts\installer'
$appDir = Join-Path $outDir 'app'

function Find-Iscc {
    $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $searchRoots = @(
        (Join-Path $env:LOCALAPPDATA 'Programs'),
        ${env:ProgramFiles(x86)},
        $env:ProgramFiles
    ) | Where-Object { $_ }
    foreach ($dir in $searchRoots) {
        $found = Get-ChildItem -Path $dir -Filter 'Inno Setup *' -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'ISCC.exe' } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($found) { return $found }
    }
    return $null
}

function Invoke-Step([string]$what, [scriptblock]$action) {
    Write-Host "==> $what" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE) { throw "$what failed (exit code $LASTEXITCODE)." }
}

$iscc = Find-Iscc
if (-not $iscc) {
    throw "Inno Setup's ISCC.exe was not found. Install it with: winget install --id JRSoftware.InnoSetup -e"
}

$version = (& dotnet msbuild (Join-Path $root 'src\DifFuse.App\DifFuse.App.csproj') -getProperty:Version).Trim()
if ($LASTEXITCODE -or -not $version) { throw 'Could not read the version from the project.' }
Write-Host "DifFuse $version" -ForegroundColor Cyan

if (-not $SkipTests) {
    Invoke-Step 'Running tests' { dotnet test (Join-Path $root 'DifFuse.slnx') }
}

if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }
Invoke-Step 'Publishing self-contained win-x64 build' {
    dotnet publish (Join-Path $root 'src\DifFuse.App') -c Release -r win-x64 --self-contained -p:DebugType=none -o $appDir
}

Invoke-Step 'Building installer' {
    & $iscc /Q "/DAppVersion=$version" "/DSourceDir=$appDir" "/O$outDir" (Join-Path $PSScriptRoot 'DifFuse.iss')
}

$setup = Get-Item (Join-Path $outDir "DifFuse-Setup-$version.exe")
Write-Host ("Installer: {0} ({1:N0} MB)" -f $setup.FullName, ($setup.Length / 1MB)) -ForegroundColor Green
