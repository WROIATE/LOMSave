$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @(
    (Join-Path $root 'src/Battle/TeamBattleSkip.cs'),
    (Join-Path $root 'src/Battle/BattleRewardPolicy.cs'),
    (Join-Path $PSScriptRoot 'TeamBattleSkipTests.cs')
) -CompilerOptions '/nowarn:0649'
[LOMSave.Probe.TeamBattleSkipTests]::Run()
