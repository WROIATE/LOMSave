$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$json = Join-Path $PSHOME 'Newtonsoft.Json.dll'
$references = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName) + @($json)
Add-Type -Path @(
    (Join-Path $root 'src/Saves/LocalJsonFile.cs'),
    (Join-Path $root 'src/Calendar/CalendarStore.cs'),
    (Join-Path $root 'src/Calendar/CalendarEventJournal.cs'),
    (Join-Path $root 'src/Events/EventSelectionStore.cs'),
    (Join-Path $root 'src/Battle/BattleRewardPolicy.cs'),
    (Join-Path $root 'src/Minigames/PinballRewards.cs'),
    (Join-Path $PSScriptRoot 'FeatureDataTests.cs')
) -ReferencedAssemblies $references -CompilerOptions '/nowarn:1701,0649'
[LOMSave.Probe.FeatureDataTests]::Run((Join-Path $root ('obj/feature-tests/' + [Guid]::NewGuid().ToString('N'))))
