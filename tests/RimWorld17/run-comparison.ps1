param([int]$Repeats = 3)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$evidenceRoot = Join-Path $repoRoot 'docs/animal-behavior-1.7/validation'
$logPath = Join-Path $env:USERPROFILE 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Logs/Player.log'
foreach ($mode in @('updated','baseline','no-mod')) {
    & (Join-Path $PSScriptRoot 'run-benchmark.ps1') -Mode $mode -Repeats $Repeats -Run
    if ($LASTEXITCODE) { throw "Benchmark launch failed: $mode" }
    $profileName = if ($mode -eq 'no-mod') { 'ZoologyBenchmarkNoMod17' } else { 'ZoologyBenchmark17' }
    $resultPath = Join-Path $env:LOCALAPPDATA "Temp/$profileName/performance.txt"
    $deadline = (Get-Date).AddMinutes(10)
    while ($true) {
        if ((Get-Date) -gt $deadline) { throw "Benchmark timeout: $mode" }
        if (Test-Path -LiteralPath $resultPath) {
            $result = Get-Content -LiteralPath $resultPath -Raw
            if ($result -match '(?m)^EXCEPTION') { throw $result }
            if ($result -match "(?m)^COMPLETE $mode") { break }
        }
        Start-Sleep -Seconds 1
    }
    Copy-Item -LiteralPath $resultPath -Destination (Join-Path $evidenceRoot "performance-$mode-final.txt") -Force
    Copy-Item -LiteralPath $logPath -Destination (Join-Path $repoRoot "Zoology/Source/codex-build/implementation/performance-$mode-final.log") -Force
    Write-Output $result
}
