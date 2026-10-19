$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @(
    (Join-Path $root 'src/Replay/StoryCommandTrace.cs'),
    (Join-Path $root 'src/Replay/ReplayMissionGuard.cs'),
    (Join-Path $PSScriptRoot 'StoryReplayTests.cs')
)
[LOMSave.Probe.StoryReplayTests]::Run()
