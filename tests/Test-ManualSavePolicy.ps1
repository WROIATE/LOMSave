$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -Path @((Join-Path $root 'src/Saves/ManualSavePolicy.cs'),(Join-Path $PSScriptRoot 'ManualSavePolicyTests.cs'))
[LOMSave.Probe.ManualSavePolicyTests]::Run()
