$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$references = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName) + @((Join-Path $PSHOME 'Newtonsoft.Json.dll'))
Add-Type -Path @(
    (Join-Path $root 'src/Saves/LocalJsonFile.cs'),
    (Join-Path $root 'src/Saves/StorySaveFiles.cs'),
    (Join-Path $root 'src/Saves/ActionAutoSavePolicy.cs'),
    (Join-Path $root 'src/Calendar/CalendarStore.cs'),
    (Join-Path $root 'src/Calendar/CalendarEventJournal.cs'),
    (Join-Path $root 'src/Calendar/CalendarEventLog.cs'),
    (Join-Path $root 'src/Calendar/ActionCalendar.cs'),
    (Join-Path $PSScriptRoot 'CalendarRuntimeTests.cs')
) -ReferencedAssemblies $references -CompilerOptions '/nowarn:1701,0649'
[LOMSave.Probe.CalendarRuntimeTests]::Run((Join-Path $root ('obj/calendar-runtime-tests/' + [Guid]::NewGuid().ToString('N'))))
