$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @((Join-Path $root 'src/Debug/DebugMenuShortcut.cs'), (Join-Path $PSScriptRoot 'DebugMenuShortcutTests.cs')) -CompilerOptions '/nowarn:0649'
[LOMSave.Probe.DebugMenuShortcutTests]::Run()
