param([string]$StoryDirectory = 'obj/conditional-events-0.25.21/story', [string]$ReportPath = 'obj/event-inspector-coverage.json')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# Exercise the production Harmony hooks through the same mission-before-pool
# sequence as PositionController.OnPositionClick, without Unity's native runtime.
Add-Type -Path @(
    (Join-Path $root 'src/Events/EventInspector.cs'),
    (Join-Path $root 'src/Calendar/StoryEventRoutes.cs'),
    (Join-Path $root 'src/Events/BranchEventSelection.cs'),
    (Join-Path $root 'src/Events/BranchPossibilities.cs'),
    (Join-Path $root 'src/Events/NativeBranchConditions.cs'),
    (Join-Path $root 'src/Events/IndexedEventPreview.cs'),
    (Join-Path $root 'src/Events/StoryEventIndexData.cs'),
    (Join-Path $root 'src/Events/StoryEventConfigs.cs'),
    (Join-Path $root 'src/Events/EventPreviewNames.cs'),
    (Join-Path $root 'src/Events/EventSelectionStore.cs'),
    (Join-Path $root 'src/Options/LuaImpactScanner.cs'),
    (Join-Path $root 'src/Options/LuaRouteLiterals.cs'),
    (Join-Path $PSScriptRoot 'EventInspectorTests.cs'),
    (Join-Path $PSScriptRoot 'ConditionalEventTests.cs')
) -ReferencedAssemblies (@(Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName) + @((Join-Path $PSHOME 'Newtonsoft.Json.dll'))) -CompilerOptions '/nowarn:1701,0649'
[LOMSave.Probe.EventInspectorTests]::Run()

[LOMSave.Probe.ConditionalEventTests]::Run((Join-Path $root 'artifacts/LOMSaveProbe/LOMSaveProbe.StoryEvents.json'),$StoryDirectory,(Join-Path $root $ReportPath))
