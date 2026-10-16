$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @((Join-Path $root 'src/Replay/RecallShortcut.cs'), (Join-Path $PSScriptRoot 'RecallShortcutTests.cs')) -CompilerOptions '/nowarn:0649'
[LOMSave.Probe.RecallShortcutTests]::Run()
