param([string]$StoryDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$StoryDirectory) { $StoryDirectory = Join-Path $root 'obj/game-update-20261015/story' }
$references = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName) + @((Join-Path $PSHOME 'Newtonsoft.Json.dll'))
Add-Type -Path @(
    (Join-Path $root 'src/Saves/LocalJsonFile.cs'),
    (Join-Path $root 'src/Calendar/CalendarEventJournal.cs'),
    (Join-Path $root 'src/Calendar/CalendarEventLog.cs'),
    (Join-Path $root 'src/Events/EventInspector.cs'),
    (Join-Path $root 'src/Events/StoryEventConfigs.cs'),
    (Join-Path $root 'src/Events/StoryEventIndexData.cs'),
    (Join-Path $root 'src/Events/EventPreviewNames.cs'),
    (Join-Path $root 'src/Events/EventSelectionStore.cs'),
    (Join-Path $root 'src/Options/LuaImpactScanner.cs'),
    (Join-Path $root 'src/Calendar/StoryEventRoutes.cs'),
    (Join-Path $root 'src/Calendar/CalendarStoryEvents.cs'),
    (Join-Path $PSScriptRoot 'EventInspectorTests.cs'),
    (Join-Path $PSScriptRoot 'CalendarStoryEventsTests.cs')
) -ReferencedAssemblies $references -CompilerOptions @('/nowarn:1701,0649', '/define:CALENDAR_JOURNAL')
[LOMSave.Probe.CalendarStoryEventsTests]::Run($StoryDirectory, (Join-Path $root ('obj/calendar-events-tests/' + [Guid]::NewGuid().ToString('N'))))
