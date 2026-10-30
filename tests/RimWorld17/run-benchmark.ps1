param([ValidateSet('baseline','updated','no-mod')][string]$Mode = 'updated', [string]$Label = '', [int]$Repeats = 3, [int]$LogicalCpu = -1, [switch]$Run,
    [string]$GameRoot = '', [ValidateSet('normal','animals-1000','mass-scavenging','mass-scavenging-ingest','mass-pets','mass-roamers','mass-families','predator-prey','mass-combat','pig-farm','pig-farm-fixed','combined-stress','all')][string]$Scenario = 'normal')
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$GameRoot) { $GameRoot = Join-Path $repoRoot 'Zoology/Source/codex-build/implementation/game17-sandbox' }
$gameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
if ($gameRoot -eq 'D:\SteamLibrary\steamapps\common\RimWorld') { throw 'Benchmark must use an isolated game copy' }
$profileRoot = Join-Path $env:LOCALAPPDATA ('Temp/' + $(if ($Mode -eq 'no-mod') { 'ZoologyBenchmarkNoMod17' } else { 'ZoologyBenchmark17' }))
$modRoot = Join-Path $gameRoot ('Mods/' + $(if ($Mode -eq 'no-mod') { 'ZoologyCodexBenchmarkProbe17' } else { 'ZoologyCodexBenchmark17' }))
Get-CimInstance Win32_Process -Filter "Name = 'RimWorld.exe'" | Where-Object { $_.CommandLine -like '*-savedatafolder=*ZoologyBenchmark*' } | ForEach-Object {
    Stop-Process -Id $_.ProcessId -PassThru | Wait-Process -Timeout 15
}
dotnet build (Join-Path $PSScriptRoot 'PerformanceProbe/PerformanceProbe.csproj') -c Release --nologo -v:q
if ($LASTEXITCODE) { throw 'Probe build failed' }
if ($Mode -eq 'baseline') {
    $extractRoot = Join-Path $repoRoot 'Zoology/Source/codex-build/implementation/benchmark-baseline-assets'
    if (!(Test-Path -LiteralPath $extractRoot)) { Expand-Archive -LiteralPath (Join-Path $repoRoot 'Zoology/Source/codex-build/implementation/baseline-assets.zip') -DestinationPath $extractRoot }
    $assetRoot = Join-Path $extractRoot 'Zoology'
    $dllPath = Join-Path $repoRoot 'Zoology/Source/codex-build/implementation/baseline-release/ZoologyMod.dll'
} elseif ($Mode -eq 'updated') {
    $assetRoot = Join-Path $repoRoot 'Zoology'
    $dllPath = Join-Path $repoRoot 'Zoology/Source/codex-build/implementation/build17/ZoologyMod.dll'
}
if ($Mode -ne 'no-mod') {
    # Old experiment folders are replaced only after their absolute path is checked.
    if ((Split-Path $modRoot -Parent) -ne (Join-Path $gameRoot 'Mods')) { throw 'Unexpected benchmark path' }
    if (Test-Path -LiteralPath $modRoot) { Remove-Item -LiteralPath $modRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $modRoot,(Join-Path $modRoot 'Assemblies') | Out-Null
    Get-ChildItem -LiteralPath $assetRoot | Where-Object { $_.Name -notin @('Source','Assemblies','1.7') } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $modRoot -Recurse -Force }
    [xml]$about = Get-Content -LiteralPath (Join-Path $modRoot 'About/About.xml')
    $about.ModMetaData.packageId = 'com.abobashark.zoologymod.codexbenchmark17'; $about.Save((Join-Path $modRoot 'About/About.xml'))
    Copy-Item -LiteralPath $dllPath -Destination (Join-Path $modRoot 'Assemblies/ZoologyMod.dll') -Force
    if ($Mode -eq 'updated') {
        New-Item -ItemType Directory -Force -Path (Join-Path $modRoot 'Core/Defs/ZoologyValidation') | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Fixtures.xml') -Destination (Join-Path $modRoot 'Core/Defs/ZoologyValidation/Fixtures.xml') -Force
    }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PerformanceProbe/bin/Release/net10.0/ZoologyPerformanceProbe.dll') -Destination (Join-Path $modRoot 'Assemblies/ZoologyPerformanceProbe.dll') -Force
$Mode | Set-Content -LiteralPath (Join-Path $profileRoot 'benchmark-mode.txt')
$Scenario | Set-Content -LiteralPath (Join-Path $profileRoot 'scenario.txt')
$Repeats | Set-Content -LiteralPath (Join-Path $profileRoot 'repeats.txt')
$LogicalCpu | Set-Content -LiteralPath (Join-Path $profileRoot 'benchmark-cpu.txt')
New-Item -ItemType File -Force -Path (Join-Path $profileRoot 'pin-benchmark-thread.txt') | Out-Null
if (Test-Path -LiteralPath (Join-Path $profileRoot 'performance.txt')) { Remove-Item -LiteralPath (Join-Path $profileRoot 'performance.txt') }
if ($Run) {
    $process = Start-Process -FilePath (Join-Path $gameRoot 'RimWorld.exe') -ArgumentList ('-quicktest -savedatafolder=' + $profileRoot.Replace('\','/')) -WindowStyle Hidden -PassThru
    Write-Output ('Benchmark ' + $Mode + ' ' + $Label + ' process: ' + $process.Id)
}
