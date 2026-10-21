$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$references=@(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)+@((Join-Path $PSHOME 'Newtonsoft.Json.dll'))
Add-Type -Path @((Join-Path $root 'src/Events/StoryEventIndexData.cs'),(Join-Path $root 'src/Events/StoryEventIndex.cs'),(Join-Path $PSScriptRoot 'StoryEventIndexTests.cs')) -ReferencedAssemblies $references -CompilerOptions @('/nowarn:1701,0649')
[LOMSave.Probe.StoryEventIndexTests]::Run((Join-Path $root ('obj/event-index-tests/'+[Guid]::NewGuid().ToString('N'))),(Join-Path $root 'artifacts/LOMSaveProbe/LOMSaveProbe.StoryEvents.json'))
