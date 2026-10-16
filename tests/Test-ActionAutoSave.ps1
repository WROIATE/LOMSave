$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Add-Type -Path @(
    (Join-Path $taskRoot 'src/Saves/ActionAutoSavePolicy.cs'),
    (Join-Path $PSScriptRoot 'ActionAutoSaveTests.cs')
)
[LOMSave.Probe.ActionAutoSaveTests]::Run()
