param([int]$Repeats = 3, [string]$Scenario = 'all', [int]$LogicalCpu = -1, [string]$EvidenceName = 'perf-audit-2026-10-28')
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ($EvidenceName -notmatch '^[A-Za-z0-9_-]+$') { throw 'EvidenceName must be a directory name' }
$otherGameProcesses = @(Get-CimInstance Win32_Process -Filter "Name = 'RimWorld.exe'" | Where-Object {
    $_.CommandLine -notlike '*-savedatafolder=*ZoologyBenchmark*'
})
if ($otherGameProcesses.Count -gt 0) {
    throw 'Other RimWorld processes are running; close them before the comparison series.'
}
$evidenceRoot = Join-Path $repoRoot ('docs/animal-behavior-1.7/validation/' + $EvidenceName)
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
$logPath = Join-Path $env:USERPROFILE 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Logs/Player.log'
foreach ($mode in @('no-mod','baseline','updated')) {
    & (Join-Path $PSScriptRoot 'run-benchmark.ps1') -Mode $mode -Scenario $Scenario -Repeats $Repeats -LogicalCpu $LogicalCpu -Run
    if ($LASTEXITCODE) { throw "Benchmark launch failed: $mode" }
    $profileName = if ($mode -eq 'no-mod') { 'ZoologyBenchmarkNoMod17' } else { 'ZoologyBenchmark17' }
    $resultPath = Join-Path $env:LOCALAPPDATA "Temp/$profileName/performance.txt"
    $deadline = (Get-Date).AddMinutes(30)
    while ($true) {
        if ((Get-Date) -gt $deadline) { throw "Benchmark timeout: $mode" }
        if (Test-Path -LiteralPath $resultPath) {
            $result = Get-Content -LiteralPath $resultPath -Raw
            if ($result -match '(?m)^EXCEPTION') { throw $result }
            if ($result -match "(?m)^COMPLETE $mode") { break }
        }
        Start-Sleep -Seconds 1
    }
    Copy-Item -LiteralPath $resultPath -Destination (Join-Path $evidenceRoot "performance-$mode-$Scenario.txt") -Force
    Copy-Item -LiteralPath $logPath -Destination (Join-Path $evidenceRoot "player-$mode-$Scenario.log") -Force
    Write-Output $result
}
