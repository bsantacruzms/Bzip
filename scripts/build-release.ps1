#requires -Version 5.1
<#
.SYNOPSIS
    Builds portable single-file BoltZip executables (GUI + CLI) into dist/.
.DESCRIPTION
    Publishes self-contained, single-file win-x64 builds with compression.
    Produces:
      dist/BoltZipTool-<version>-portable.exe  (WPF GUI)
      dist/bz-<version>-portable.exe           (CLI)
#>
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

# Ensure dotnet is reachable even if the shell reset PATH.
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')

$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { $version = '0.0.0' }
Write-Host "BoltZip $version - publishing portable executables ($Runtime)" -ForegroundColor Cyan

$dist = Join-Path $root 'dist'
$publish = Join-Path $root 'publish'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue

$common = @(
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', 'true',
    '/p:PublishSingleFile=true',
    '/p:IncludeNativeLibrariesForSelfExtract=true',
    '/p:IncludeAllContentForSelfExtract=true',
    '/p:EnableCompressionInSingleFile=true',
    '/p:DebugType=none'
)

function Publish-Project {
    param([string]$Project, [string]$ExeName, [string]$OutName)

    $outDir = Join-Path $publish $OutName
    Write-Host "Publishing $Project" -ForegroundColor DarkCyan
    dotnet publish $Project @common -o $outDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $Project" }

    $source = Join-Path $outDir $ExeName
    $target = Join-Path $dist "$OutName-$version-portable.exe"
    Copy-Item $source $target -Force
    $size = [math]::Round((Get-Item $target).Length / 1MB, 1)
    Write-Host "  -> $target ($size MB)" -ForegroundColor Green
}

Publish-Project 'src/BoltZip.App/BoltZip.App.csproj' 'BoltZipTool.exe' 'BoltZipTool'
Publish-Project 'src/BoltZip.Cli/BoltZip.Cli.csproj' 'bz.exe' 'bz'

Write-Host "Done. Portable builds are in $dist" -ForegroundColor Cyan
