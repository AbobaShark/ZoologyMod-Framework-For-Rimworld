param(
    [string]$GameRoot = 'D:\SteamLibrary\steamapps\common\RimWorld',
    [string]$HarmonyRoot = '',
    [switch]$Run,
    [switch]$Benchmark,
    [string]$BenchmarkLabel = 'baseline',
    [switch]$ReuseBenchmarkSave,
    [switch]$UseBeforeAssembly
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
if (!(Get-Content -LiteralPath (Join-Path $GameRoot 'Version.txt')).StartsWith('1.7.')) { throw 'RimWorld 1.7 is required' }
if (!$HarmonyRoot) { $HarmonyRoot = Join-Path $GameRoot '../../workshop/content/294100/2009463077' }
$HarmonyRoot = (Resolve-Path -LiteralPath $HarmonyRoot).Path
$isolated = Join-Path $repo 'Zoology/Source/codex-build/barn-validation-game'
$profile = Join-Path $env:LOCALAPPDATA 'Temp/ZoologyBarnValidation17'
if ($Benchmark) { $profile = Join-Path $env:LOCALAPPDATA 'Temp/ZoologyBarnPerformance17' }
# Stop only a prior instance of this isolated test profile.
Get-CimInstance Win32_Process -Filter "Name = 'RimWorld.exe'" | Where-Object {
    $_.CommandLine -like ('*-savedatafolder=' + $profile.Replace('\','/') + '*')
} | ForEach-Object { Stop-Process -Id $_.ProcessId; Wait-Process -Id $_.ProcessId -Timeout 15 -ErrorAction SilentlyContinue }
dotnet build (Join-Path $repo 'Zoology/Source/ZoologyMod.csproj') -c Release -p:RimWorldVersion=1.7 "-p:GameRoot=$GameRoot" -p:OutputPath=codex-build/implementation/build17 --nologo -v:q
if ($LASTEXITCODE) { throw '1.7 mod build failed' }
dotnet build (Join-Path $PSScriptRoot 'BarnValidation.csproj') -c Release "-p:GameRoot=$GameRoot" --nologo -v:q
if ($LASTEXITCODE) { throw 'Test fixture build failed' }
New-Item -ItemType Directory -Force -Path $isolated,(Join-Path $isolated 'Mods'),(Join-Path $profile 'Config') | Out-Null
# Read-only game assets are shared. All mods, saves and settings are isolated.
Get-ChildItem -LiteralPath $GameRoot -File | ForEach-Object {
    $destination = Join-Path $isolated $_.Name
    if (!(Test-Path -LiteralPath $destination)) { New-Item -ItemType HardLink -Path $destination -Target $_.FullName | Out-Null }
}
if ((Get-Content (Join-Path $isolated 'Version.txt')) -ne (Get-Content (Join-Path $GameRoot 'Version.txt'))) { throw 'Isolated game is stale; use a fresh isolated directory after updating the game' }
foreach ($name in @('Data','Rendering','vulkan')) {
    $destination = Join-Path $isolated $name
    if (!(Test-Path -LiteralPath $destination)) { New-Item -ItemType Junction -Path $destination -Target (Join-Path $GameRoot $name) | Out-Null }
}
$mod = Join-Path $isolated 'Mods/ZoologyBarnValidation17'
New-Item -ItemType Directory -Force $mod | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repo 'Zoology') | Where-Object { $_.Name -ne 'Source' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $mod -Recurse -Force }
Copy-Item -LiteralPath (Join-Path $repo 'Zoology/Source/codex-build/implementation/build17/ZoologyMod.dll') -Destination (Join-Path $mod '1.7/Assemblies/ZoologyMod.dll') -Force
if ($UseBeforeAssembly) {
    Copy-Item -LiteralPath (Join-Path $repo 'Zoology/Source/codex-build/barn-perf-before/ZoologyMod.dll') -Destination (Join-Path $mod '1.7/Assemblies/ZoologyMod.dll') -Force
}
Copy-Item -LiteralPath (Join-Path $repo 'Zoology/Source/codex-build/barn-fixture/ZoologyBarnValidation17.dll') -Destination (Join-Path $mod '1.7/Assemblies/ZoologyBarnValidation17.dll') -Force
[xml]$about = Get-Content -LiteralPath (Join-Path $mod 'About/About.xml')
$about.ModMetaData.packageId = 'com.abobashark.zoologymod.barnvalidation17'
$about.ModMetaData.name = 'Zoology Barn Validation 1.7'
$about.Save((Join-Path $mod 'About/About.xml'))
$harmonyDest = Join-Path $isolated 'Mods/BarnTestHarmony'
New-Item -ItemType Directory -Force $harmonyDest | Out-Null
Get-ChildItem -LiteralPath $HarmonyRoot | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $harmonyDest -Recurse -Force }
@'
<ModsConfigData><version>1.7</version><activeMods>
<li>brrainz.harmony</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>com.abobashark.zoologymod.barnvalidation17</li>
</activeMods></ModsConfigData>
'@ | Set-Content -Encoding utf8 (Join-Path $profile 'Config/ModsConfig.xml')
if ($Benchmark) {
    $dllHash = (Get-FileHash -LiteralPath (Join-Path $mod '1.7/Assemblies/ZoologyMod.dll')).Hash
    "$BenchmarkLabel DLLSHA256=$dllHash" | Set-Content (Join-Path $profile 'barn-performance-enabled.txt')
    $stage = 'setup'
    if ($ReuseBenchmarkSave) {
        if (!(Test-Path (Join-Path $profile 'Saves/ZoologyBarnPerformanceBase.rws'))) { throw 'Benchmark base save missing' }
        $stage = 'load-base'
    }
    $stage | Set-Content (Join-Path $profile 'barn-performance-stage.txt')
} else { '1' | Set-Content (Join-Path $profile 'barn-only.txt') }
if ($Run) {
    $result = Join-Path $profile 'barn-validation.txt'
    if ($Benchmark) { $result = Join-Path $profile 'barn-performance.txt' }
    if (Test-Path -LiteralPath $result) { Move-Item -LiteralPath $result -Destination (Join-Path $profile ('barn-validation-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')) }
    $process = Start-Process -FilePath (Join-Path $isolated 'RimWorld.exe') -ArgumentList ('-quicktest -savedatafolder=' + $profile.Replace('\','/')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $profile 'stdout.log') -RedirectStandardError (Join-Path $profile 'stderr.log')
    Write-Output "PID=$($process.Id) Report=$result"
} else { Write-Output "Prepared isolated test game: $isolated" }
