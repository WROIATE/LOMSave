$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @(
    (Join-Path $root 'LuaImpactScanner.cs'),
    (Join-Path $root 'DialoguePacer.cs'),
    (Join-Path $root 'DialogueTiming.cs'),
    (Join-Path $root 'SaveNoteStore.cs'),
    (Join-Path $root 'CheckpointNotificationBuffer.cs'),
    (Join-Path $root 'CheckpointNameRules.cs'),
    (Join-Path $PSScriptRoot 'DialogueAndImpactTests.cs')
)
[LOMSave.Probe.DialogueAndImpactTests]::Run((Join-Path $root 'obj\story'))
