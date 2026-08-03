#requires -Version 5.1
<#
  Benchmarks BoltZip against other archivers on ALREADY-COMPRESSED media (video, photos,
  music). This is the honest "video compression" story: no lossless archiver can meaningfully
  shrink encoded media, so every tool lands near 100% of the original size. What differs is
  time — BoltZip detects already-compressed media and stores it at full speed instead of
  burning CPU trying to compress the incompressible. High-entropy random bytes are used as a
  faithful stand-in for encoded media (which is ~incompressible to a general-purpose codec).
  Writes docs/assets/benchmark-media.json for the showcase site. Missing tools are skipped.
#>
param(
    [int]$SizeMB = 120,
    [ValidateRange(1, 9)]
    [int]$Iterations = 3,
    [string]$Bz = "$PSScriptRoot\..\dist\bz-1.1.5-portable.exe",
    [string]$SevenZip = "C:\Program Files\7-Zip\7z.exe",
    [string]$OutJson = "$PSScriptRoot\..\docs\assets\benchmark-media.json"
)
$ErrorActionPreference = 'Stop'

$work = Join-Path $env:TEMP ("bzmedia_" + [guid]::NewGuid().ToString('N'))
$data = Join-Path $work 'media'
New-Item -ItemType Directory -Force $data | Out-Null
Write-Host "Generating ~$SizeMB MB already-compressed media sample ..."

# Encoded video/photos/music are ~incompressible to a general-purpose codec, so random bytes
# are a faithful proxy. Named with real media extensions so BoltZip's media fast path engages.
function New-RandomFile($path, $bytes) {
    $buf = New-Object byte[] $bytes
    (New-Object Random).NextBytes($buf)
    [IO.File]::WriteAllBytes($path, $buf)
}
New-RandomFile (Join-Path $data 'video.mp4')  ([int]($SizeMB * 0.60) * 1MB)
New-RandomFile (Join-Path $data 'photos.jpg') ([int]($SizeMB * 0.25) * 1MB)
New-RandomFile (Join-Path $data 'music.mp3')  ([int]($SizeMB * 0.15) * 1MB)

$datasetMB = [math]::Round(((Get-ChildItem $data -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "Media dataset: $datasetMB MB`n"

$outDir = Join-Path $work 'out'
New-Item -ItemType Directory -Force $outDir | Out-Null
$results = @()

if (Test-Path $Bz) {
    Write-Host "Warming up ...`n"
    $warm = Join-Path $outDir 'warm.bz'
    & $Bz create $warm $data --goal fast -q 2>$null | Out-Null
    Remove-Item $warm -Force -EA SilentlyContinue
}

function Measure-Sec([scriptblock]$block) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $block 2>$null | Out-Null
    $sw.Stop()
    return [math]::Round($sw.Elapsed.TotalSeconds, 2)
}

function Get-Median([double[]]$values) {
    $sorted = @($values | Sort-Object)
    $middle = [int][math]::Floor($sorted.Count / 2)
    if (($sorted.Count % 2) -eq 1) { return $sorted[$middle] }
    return [math]::Round(($sorted[$middle - 1] + $sorted[$middle]) / 2, 2)
}

function Add-Result($tool, $format, $archive, [scriptblock]$compress, $extractDir, [scriptblock]$extract, [int]$Iters = 0) {
    $runs = if ($Iters -gt 0) { $Iters } else { $Iterations }
    $compressTimes = @()
    $extractTimes = @()
    for ($iteration = 1; $iteration -le $runs; $iteration++) {
        if (Test-Path $archive) { Remove-Item $archive -Force }
        if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
        New-Item -ItemType Directory -Force $extractDir | Out-Null
        $compressTimes += Measure-Sec $compress
        $extractTimes += Measure-Sec $extract
    }
    $c = Get-Median $compressTimes
    $x = Get-Median $extractTimes
    $sizeMB = [math]::Round((Get-Item $archive).Length / 1MB, 1)
    $ratio = [math]::Round(($sizeMB / $datasetMB) * 100, 1)
    $script:results += [pscustomobject]@{ tool = $tool; format = $format; compressSec = $c; extractSec = $x; sizeMB = $sizeMB; ratioPct = $ratio }
    Write-Host ("{0,-14} {1,-6} compress {2,7}s   extract {3,7}s   {4,7} MB   {5,5}%" -f $tool, $format, $c, $x, $sizeMB, $ratio)
}

if (Test-Path $Bz) {
    $a = Join-Path $outDir 'bz.bz'; $e = Join-Path $outDir 'ex_bz'
    Add-Result 'BoltZip' '.bz' $a { & $Bz create $a $data --goal balanced -q } $e { & $Bz extract $a --out $e -y -q }
}

if (Test-Path $SevenZip) {
    $a = Join-Path $outDir 's.7z'; $e = Join-Path $outDir 'ex_7z'
    Add-Result '7-Zip' '.7z' $a { & $SevenZip a -t7z -mx=5 -bso0 -bsp0 -bd $a $data } $e { & $SevenZip x $a "-o$e" -y -bso0 -bsp0 -bd }
    $az = Join-Path $outDir 's.zip'; $ez = Join-Path $outDir 'ex_7zzip'
    Add-Result '7-Zip' '.zip' $az { & $SevenZip a -tzip -mx=5 -bso0 -bsp0 -bd $az $data } $ez { & $SevenZip x $az "-o$ez" -y -bso0 -bsp0 -bd }
}

$aw = Join-Path $outDir 'w.zip'; $ew = Join-Path $outDir 'ex_win'
# Compress-Archive is single-threaded and slow; one run is enough for an illustrative number.
Add-Result 'Windows Zip' '.zip' $aw { Compress-Archive -Path (Join-Path $data '*') -DestinationPath $aw -Force } $ew { Expand-Archive -Path $aw -DestinationPath $ew -Force } -Iters 1

$report = [ordered]@{
    cpu                = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
    logicalCores       = [Environment]::ProcessorCount
    datasetMB          = $datasetMB
    datasetDescription = "already-compressed media (video/photo/audio, high-entropy)"
    iterations         = $Iterations
    statistic          = "median"
    date               = (Get-Date).ToString('yyyy-MM-dd')
    results            = $results
}
New-Item -ItemType Directory -Force (Split-Path $OutJson) | Out-Null
($report | ConvertTo-Json -Depth 5) | Set-Content $OutJson -Encoding UTF8
Write-Host "`nWrote $OutJson"
Remove-Item $work -Recurse -Force -EA SilentlyContinue
