$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @(
    (Join-Path $root 'StorySaveFiles.cs'),
    (Join-Path $PSScriptRoot 'StorySaveFilesTests.cs')
)
[LOMSave.Probe.StorySaveFilesTests]::Run((Join-Path $root 'obj/story-save-file-tests'))
