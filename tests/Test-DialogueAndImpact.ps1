param([string]$StoryDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @(
    (Join-Path $root 'src/Options/LuaImpactScanner.cs'),
    (Join-Path $root 'src/Options/StoryImpactCache.cs'),
    (Join-Path $root 'src/Options/OptionImpactText.cs'),
    (Join-Path $root 'src/Dialogue/DialoguePacer.cs'),
    (Join-Path $root 'src/Dialogue/DialogueTiming.cs'),
    (Join-Path $root 'src/Saves/SaveNoteStore.cs'),
    (Join-Path $root 'src/Checkpoints/CheckpointNotificationBuffer.cs'),
    (Join-Path $root 'src/Localization/CheckpointNameRules.cs'),
    (Join-Path $PSScriptRoot 'DialogueAndImpactTests.cs')
)
if (!$StoryDirectory) { $StoryDirectory = Join-Path $root 'obj\story' }
[LOMSave.Probe.DialogueAndImpactTests]::Run($StoryDirectory)
