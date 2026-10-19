$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Add-Type -Path @(
    (Join-Path $taskRoot 'src/Saves/ActionAutoSave.cs'),
    (Join-Path $taskRoot 'src/Saves/ActionAutoSavePolicy.cs'),
    (Join-Path $PSScriptRoot 'ActionAutoSaveRuntimeTests.cs')
)
[LOMSave.Probe.ActionAutoSaveRuntimeTests]::Run()
