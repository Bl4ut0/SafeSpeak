param(
    [int]$ProcessId = 0,
    [ValidateRange(5, 3600)][int]$DurationSeconds = 30
)
$ErrorActionPreference = 'Stop'
if ($ProcessId -eq 0) {
    $matches = @(Get-Process -Name SafeSpeak.App, SafeSpeak -ErrorAction SilentlyContinue)
    if ($matches.Count -ne 1) { throw 'Start SafeSpeak first, or specify -ProcessId when multiple instances are running.' }
    $ProcessId = $matches[0].Id
}
$target = Get-Process -Id $ProcessId
$target.Refresh()
$cpuBefore = $target.TotalProcessorTime.TotalMilliseconds
$samples = [System.Collections.Generic.List[object]]::new()
$timer = [System.Diagnostics.Stopwatch]::StartNew()
$previousCpu = $cpuBefore
$previousWall = 0.0
$exitedDuringMeasurement = $false
while ($timer.Elapsed.TotalSeconds -lt $DurationSeconds) {
    Start-Sleep -Milliseconds 1000
    $target.Refresh()
    if ($target.HasExited) { $exitedDuringMeasurement = $true; break }
    $wall = $timer.Elapsed.TotalMilliseconds
    $cpu = $target.TotalProcessorTime.TotalMilliseconds
    $samples.Add([pscustomobject]@{
        ElapsedSeconds = $wall / 1000
        ProcessCpuPercent = 100 * ($cpu - $previousCpu) / (($wall - $previousWall) * [Environment]::ProcessorCount)
        WorkingSetMb = $target.WorkingSet64 / 1MB
        PrivateBytesMb = $target.PrivateMemorySize64 / 1MB
        Threads = $target.Threads.Count
    })
    $previousCpu = $cpu
    $previousWall = $wall
}
if ($samples.Count -eq 0) { throw 'SafeSpeak exited before the first sample.' }
$report = [pscustomobject]@{
    ProcessId = $ProcessId
    ProcessName = $target.ProcessName
    ExitedDuringMeasurement = $exitedDuringMeasurement
    LogicalProcessors = [Environment]::ProcessorCount
    DurationSeconds = $previousWall / 1000
    CpuTimeMilliseconds = $previousCpu - $cpuBefore
    AverageProcessCpuPercent = 100 * ($previousCpu - $cpuBefore) / ($previousWall * [Environment]::ProcessorCount)
    Scope = 'Only the selected process; excludes all other applications and child processes.'
    Samples = $samples
}
$outputDirectory = Join-Path $PSScriptRoot '../../artifacts/performance'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$outputPath = Join-Path $outputDirectory ('app-process-' + (Get-Date -Format yyyyMMdd-HHmmss) + '.json')
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $outputPath -Encoding utf8
$report | Select-Object ProcessName, ProcessId, DurationSeconds, AverageProcessCpuPercent, CpuTimeMilliseconds
Write-Output "Report: $outputPath"
