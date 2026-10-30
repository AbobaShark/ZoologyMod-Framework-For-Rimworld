param([string]$GameRoot = '', [switch]$Run, [switch]$CombatExtended, [string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$GameRoot) { $GameRoot = Join-Path $repoRoot 'Zoology/Source/codex-build/implementation/game17-sandbox' }
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
if ($GameRoot -eq 'D:\SteamLibrary\steamapps\common\RimWorld') { throw 'Validation must use an isolated game copy' }
$testModRoot = Join-Path $GameRoot 'Mods/ZoologyCodexValidation17'
$profileRoot = Join-Path $env:LOCALAPPDATA $(if ($CombatExtended) { 'Temp/ZoologyValidationCE17' } else { 'Temp/ZoologyValidation17' })
# Only stop this script's isolated process, never another game/profile.
Get-CimInstance Win32_Process -Filter "Name = 'RimWorld.exe'" | Where-Object {
    $_.CommandLine -like '*-savedatafolder=*ZoologyValidation*'
} | ForEach-Object { Stop-Process -Id $_.ProcessId -PassThru | Wait-Process -Timeout 15 }
dotnet build (Join-Path $repoRoot 'Zoology/Source/ZoologyMod.csproj') -c $Configuration -p:RimWorldVersion=1.7 -p:OutputPath=codex-build/implementation/build17 --nologo -v:q
if ($LASTEXITCODE) { throw 'Mod build failed' }
dotnet build (Join-Path $PSScriptRoot 'RuntimeValidation.csproj') -c $Configuration --nologo -v:q
if ($LASTEXITCODE) { throw 'Fixture build failed' }
New-Item -ItemType Directory -Force -Path $testModRoot,(Join-Path $testModRoot 'Assemblies'),(Join-Path $testModRoot '1.7/Assemblies'),(Join-Path $testModRoot 'Core/Defs/ZoologyValidation') | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Zoology') | Where-Object { $_.Name -notin @('Source','Assemblies','1.7') } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $testModRoot -Recurse -Force
}
$aboutPath = Join-Path $testModRoot 'About/About.xml'
[xml]$about = Get-Content -LiteralPath $aboutPath
$about.ModMetaData.packageId = 'com.abobashark.zoologymod.codexvalidation17'
$about.ModMetaData.name = 'Zoology Codex Validation 1.7'
$about.Save($aboutPath)
Copy-Item -LiteralPath (Join-Path $repoRoot 'Zoology/Assemblies/ZoologyMod.dll') -Destination (Join-Path $testModRoot 'Assemblies/ZoologyMod.dll') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'Zoology/Source/codex-build/implementation/build17/ZoologyMod.dll') -Destination (Join-Path $testModRoot '1.7/Assemblies/ZoologyMod.dll') -Force
# Exercise the shipped layout: legacy assembly at root, 1.7 override selected by LoadFolders.
foreach ($relative in @(("tests/RimWorld17/bin/$Configuration/net10.0/ZoologyRuntimeValidation.dll"))) {
    $sourcePath = Join-Path $repoRoot $relative
    $destinationPath = Join-Path $testModRoot ('Assemblies/' + [IO.Path]::GetFileName($sourcePath))
    $copyDeadline = (Get-Date).AddSeconds(15)
    do {
        try { Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force; break }
        catch { if ((Get-Date) -gt $copyDeadline) { throw }; Start-Sleep -Milliseconds 200 }
    } while ($true)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Fixtures.xml') -Destination (Join-Path $testModRoot 'Core/Defs/ZoologyValidation/Fixtures.xml') -Force
if ($Run) {
    $resultPath = Join-Path $profileRoot 'runtime-validation.txt'
    if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
    if (!(Test-Path -LiteralPath (Join-Path $profileRoot 'Config/ModsConfig.xml'))) { throw 'Create the isolated validation profile config first' }
    $process = Start-Process -FilePath (Join-Path $GameRoot 'RimWorld.exe') -ArgumentList ('-quicktest -savedatafolder=' + $profileRoot.Replace('\','/')) -WindowStyle Hidden -PassThru
    $process.Id | Set-Content -LiteralPath (Join-Path $repoRoot 'Zoology/Source/codex-build/implementation/game-pid.txt')
    Write-Output ('Validation process: ' + $process.Id)
}
