$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @((Join-Path $root 'src/Replay/AudioSceneState.cs'), (Join-Path $PSScriptRoot 'AudioSceneStateTests.cs'))
[LOMSave.Probe.AudioSceneStateTests]::Run()
