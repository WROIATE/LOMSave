$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @(
    (Join-Path $root 'src/Saves/StorySaveFiles.cs'),
    (Join-Path $PSScriptRoot 'StorySaveFilesTests.cs')
)
[LOMSave.Probe.StorySaveFilesTests]::Run((Join-Path $root 'obj/story-save-file-tests'))
