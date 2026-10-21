$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# Exercise the production Harmony hooks through the same mission-before-pool
# sequence as PositionController.OnPositionClick, without Unity's native runtime.
Add-Type -Path @(
    (Join-Path $root 'src/Events/EventInspector.cs'),
    (Join-Path $root 'src/Events/EventPreviewNames.cs'),
    (Join-Path $root 'src/Events/EventSelectionStore.cs'),
    (Join-Path $root 'src/Options/LuaImpactScanner.cs'),
    (Join-Path $PSScriptRoot 'EventInspectorTests.cs')
) -CompilerOptions '/nowarn:0649'
[LOMSave.Probe.EventInspectorTests]::Run()
