$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @((Join-Path $root 'src/Saves/SaveOriginPolicy.cs'), (Join-Path $PSScriptRoot 'SaveOriginTests.cs'))
[LOMSave.Probe.SaveOriginTests]::Run()
