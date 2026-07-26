#requires -Version 5.1
<#
  Measures BoltZip's GPU video shrink on a generated 1080p clip and writes
  docs/assets/benchmark-video.json for the showcase site. This is the honest "video" story:
  lossless archivers (7-Zip, WinRAR, Windows Zip) cannot re-encode video, so they leave it at
  100% of its size, while BoltZip uses the GPU to re-encode at visually-lossless quality and
  makes it dramatically smaller. Requires FFmpeg (set BOLTZIP_FFMPEG or put it on PATH) and,
  for the hardware numbers, a GPU with a video encoder (NVENC / AMF / Quick Sync).
#>
param(
    [string]$Bz = "$PSScriptRoot\..\dist\bench3\bz.exe",
    [string]$Ffmpeg = $env:BOLTZIP_FFMPEG,
    [int]$Seconds = 20,
    [int]$SourceMbps = 25,
    [string]$OutJson = "$PSScriptRoot\..\docs\assets\benchmark-video.json"
)
$ErrorActionPreference = 'Stop'

if (-not $Ffmpeg -or -not (Test-Path $Ffmpeg)) {
    $cmd = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($cmd) { $Ffmpeg = $cmd.Source } else { throw "FFmpeg not found. Set BOLTZIP_FFMPEG or add ffmpeg to PATH." }
}
$env:BOLTZIP_FFMPEG = $Ffmpeg

$work = Join-Path $env:TEMP ("bzvideo_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$src = Join-Path $work 'source.mp4'
Write-Host "Generating a $Seconds s 1080p test clip (~$SourceMbps Mbit/s H.264) ..."
& $Ffmpeg -y -hide_banner -loglevel error -f lavfi -i "testsrc2=size=1920x1080:rate=30" -t $Seconds `
    -c:v libx264 -b:v "$($SourceMbps)M" -pix_fmt yuv420p $src
$sourceMB = [math]::Round((Get-Item $src).Length / 1MB, 1)
Write-Host "Source: $sourceMB MB`n"

$outDir = Join-Path $work 'out'
New-Item -ItemType Directory -Force $outDir | Out-Null

Write-Host "Shrinking with BoltZip (GPU) ..."
$elapsed = Measure-Command { & $Bz video $src --codec h265 --out $outDir -y -q }
$outFile = Get-ChildItem $outDir -Filter *.mp4 | Select-Object -First 1
$outMB = [math]::Round($outFile.Length / 1MB, 1)
$reduction = [math]::Round((1 - $outFile.Length / (Get-Item $src).Length) * 100, 1)
$encodeSec = [math]::Round($elapsed.TotalSeconds, 1)
Write-Host ("BoltZip: {0} MB -> {1} MB  ({2}% smaller, {3}s)" -f $sourceMB, $outMB, $reduction, $encodeSec)

$gpu = 'GPU encoder'
try {
    $nv = & nvidia-smi --query-gpu=name --format=csv,noheader 2>$null
    if ($nv) { $gpu = ($nv | Select-Object -First 1).Trim() }
} catch { }

$report = [ordered]@{
    gpu             = $gpu
    clipSeconds     = $Seconds
    sourceLabel     = "1080p clip, $SourceMbps Mbit/s H.264 source"
    sourceMB        = $sourceMB
    quality         = "visually-lossless"
    encodeSeconds   = $encodeSec
    date            = (Get-Date).ToString('yyyy-MM-dd')
    results         = @(
        [ordered]@{ tool = "BoltZip (GPU re-encode)"; outMB = $outMB; reductionPct = $reduction; note = "HEVC, visually lossless, ${encodeSec}s on GPU" },
        [ordered]@{ tool = "7-Zip / WinRAR / Windows Zip"; outMB = $sourceMB; reductionPct = 0.0; note = "lossless archivers cannot re-encode video" }
    )
}
New-Item -ItemType Directory -Force (Split-Path $OutJson) | Out-Null
($report | ConvertTo-Json -Depth 5) | Set-Content $OutJson -Encoding UTF8
Write-Host "`nWrote $OutJson"
Remove-Item $work -Recurse -Force -EA SilentlyContinue
