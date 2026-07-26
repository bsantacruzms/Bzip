#requires -Version 5.1
<#
  Benchmarks BoltZip against the archivers installed on this machine (7-Zip, Windows
  built-in Zip) on a representative mixed dataset. Writes real numbers to
  docs/assets/benchmark.json for the showcase site. Tools that are not installed are skipped.
#>
param(
    [int]$SizeMB = 100,
    [ValidateRange(1, 9)]
    [int]$Iterations = 3,
    [string]$Bz = "$PSScriptRoot\..\dist\bz-1.0.2-portable.exe",
    [string]$SevenZip = "C:\Program Files\7-Zip\7z.exe",
    [string]$OutJson = "$PSScriptRoot\..\docs\assets\benchmark.json"
)
$ErrorActionPreference = 'Stop'

$work = Join-Path $env:TEMP ("bzbench_" + [guid]::NewGuid().ToString('N'))
$data = Join-Path $work 'dataset'
New-Item -ItemType Directory -Force $data | Out-Null
Write-Host "Generating ~$SizeMB MB mixed dataset ..."

# ~50% compressible text/logs
$textTarget = [int]($SizeMB * 0.5) * 1MB
$block = ("The quick brown fox jumps over the lazy dog. BoltZip compresses this efficiently. " * 200)
$tw = [IO.StreamWriter]::new((Join-Path $data 'application.log'))
$w = 0; while ($w -lt $textTarget) { $tw.Write($block); $w += $block.Length }
$tw.Close()

# ~25% CSV (structured)
$csvTarget = [int]($SizeMB * 0.25) * 1MB
$cw = [IO.StreamWriter]::new((Join-Path $data 'records.csv'))
$cw.WriteLine("id,name,email,amount,timestamp")
$i = 0; $cwritten = 0
while ($cwritten -lt $csvTarget) {
    $line = "$i,User$i,user$i@example.com,$((($i * 7) % 1000)).$((($i * 3) % 100)),2026-07-19T10:$(($i % 60).ToString('00')):00Z"
    $cw.WriteLine($line); $cwritten += $line.Length + 2; $i++
}
$cw.Close()

# ~25% incompressible binary
$randTarget = [int]($SizeMB * 0.25) * 1MB
$buf = New-Object byte[] $randTarget
(New-Object Random).NextBytes($buf)
[IO.File]::WriteAllBytes((Join-Path $data 'media.bin'), $buf)

$datasetMB = [math]::Round(((Get-ChildItem $data -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "Dataset: $datasetMB MB`n"

$outDir = Join-Path $work 'out'
New-Item -ItemType Directory -Force $outDir | Out-Null
$results = @()

# Warm up: extract the single-file runtime and warm the OS disk cache so the first
# measured run isn't penalised (fairer for every tool).
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

function Add-Result($tool, $format, $archive, [scriptblock]$compress, $extractDir, [scriptblock]$extract) {
    $compressTimes = @()
    $extractTimes = @()
    for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
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
    $a2 = Join-Path $outDir 'bzf.bz'; $e2 = Join-Path $outDir 'ex_bzf'
    Add-Result 'BoltZip (fast)' '.bz' $a2 { & $Bz create $a2 $data --goal fast -q } $e2 { & $Bz extract $a2 --out $e2 -y -q }
    $az = Join-Path $outDir 'bz.zip'; $ez = Join-Path $outDir 'ex_bzzip'
    Add-Result 'BoltZip' '.zip' $az { & $Bz create $az $data -q } $ez { & $Bz extract $az --out $ez -y -q }
}

if (Test-Path $SevenZip) {
    $a = Join-Path $outDir 's.7z'; $e = Join-Path $outDir 'ex_7z'
    Add-Result '7-Zip' '.7z' $a { & $SevenZip a -t7z -mx=5 -bso0 -bsp0 -bd $a $data } $e { & $SevenZip x $a "-o$e" -y -bso0 -bsp0 -bd }
    $az = Join-Path $outDir 's.zip'; $ez = Join-Path $outDir 'ex_7zzip'
    Add-Result '7-Zip' '.zip' $az { & $SevenZip a -tzip -mx=5 -bso0 -bsp0 -bd $az $data } $ez { & $SevenZip x $az "-o$ez" -y -bso0 -bsp0 -bd }
}

$aw = Join-Path $outDir 'w.zip'; $ew = Join-Path $outDir 'ex_win'
Add-Result 'Windows Zip' '.zip' $aw { Compress-Archive -Path (Join-Path $data '*') -DestinationPath $aw -Force } $ew { Expand-Archive -Path $aw -DestinationPath $ew -Force }

$report = [ordered]@{
    cpu                = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
    logicalCores       = [Environment]::ProcessorCount
    datasetMB          = $datasetMB
    datasetDescription = "~50% text/logs, 25% CSV, 25% incompressible binary"
    iterations         = $Iterations
    statistic          = "median"
    date               = (Get-Date).ToString('yyyy-MM-dd')
    results            = $results
}
New-Item -ItemType Directory -Force (Split-Path $OutJson) | Out-Null
($report | ConvertTo-Json -Depth 5) | Set-Content $OutJson -Encoding UTF8
Write-Host "`nWrote $OutJson"
Remove-Item $work -Recurse -Force -EA SilentlyContinue
