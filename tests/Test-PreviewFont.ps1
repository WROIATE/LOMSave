$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Add-Type -Path @(
    (Join-Path $taskRoot 'src/UI/PreviewFont.cs'),
    (Join-Path $PSScriptRoot 'PreviewFontTests.cs')
)
[LOMSave.Probe.PreviewFontTests]::Run()
