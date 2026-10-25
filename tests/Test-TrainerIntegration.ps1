$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @((Join-Path $root 'src/Options/DiceOutcomeSelection.cs'), (Join-Path $root 'src/Battle/DuelDirectVictory.cs'), (Join-Path $PSScriptRoot 'TrainerIntegrationTests.cs')) -CompilerOptions '/nowarn:0649'
[LOMSave.Probe.TrainerIntegrationTests]::Run()
