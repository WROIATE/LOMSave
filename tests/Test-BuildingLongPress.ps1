$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# Test the production pointer handler without loading Unity's native runtime.
Add-Type -Path @(
    (Join-Path $root 'BuildingEventLongPress.cs'),
    (Join-Path $root 'PreviewInteraction.cs'),
    (Join-Path $PSScriptRoot 'BuildingLongPressTests.cs')
)
[LOMSave.Probe.BuildingLongPressTests]::Run()
