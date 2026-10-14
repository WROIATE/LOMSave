$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @(
    (Join-Path $root 'ReplayAudio.cs'),
    (Join-Path $root 'ReplayRuntime.cs'),
    (Join-Path $PSScriptRoot 'ReplayRuntimeTests.cs')
)
[LOMSave.Probe.ReplayRuntimeTests]::Run()
