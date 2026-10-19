$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$json = Join-Path $PSHOME 'Newtonsoft.Json.dll'
$references = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName) + @($json)
Add-Type -Path @(
    (Join-Path $root 'src/Saves/BranchAutoSave.cs'),
    (Join-Path $root 'src/Saves/StorySaveFiles.cs'),
    (Join-Path $root 'src/Replay/StoryCommandTrace.cs'),
    (Join-Path $root 'src/Replay/ReplayMissionGuard.cs'),
    (Join-Path $root 'src/Replay/DiceRollTrace.cs'),
    (Join-Path $PSScriptRoot 'BranchAutoSaveTests.cs')
) -ReferencedAssemblies $references -CompilerOptions '/nowarn:1701'
[LOMSave.Probe.BranchAutoSaveTests]::Run((Join-Path $root ('obj/branch-save-tests/' + [Guid]::NewGuid().ToString('N'))))
