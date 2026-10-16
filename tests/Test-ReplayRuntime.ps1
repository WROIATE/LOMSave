$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @(
    (Join-Path $root 'src/Replay/ReplayAudio.cs'),
    (Join-Path $root 'src/Replay/ReplayRuntime.cs'),
    (Join-Path $PSScriptRoot 'ReplayRuntimeTests.cs')
)
[LOMSave.Probe.ReplayRuntimeTests]::Run()
