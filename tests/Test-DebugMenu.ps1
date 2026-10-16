$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$yamlLibrary = Join-Path $root 'artifacts/LOMSaveProbe/YamlDotNet.dll'
Add-Type -Path $yamlLibrary
$references = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName) + @($yamlLibrary)
Add-Type -Path @(
    (Join-Path $root 'src/Localization/CheckpointNameRules.cs'),
    (Join-Path $root 'src/Localization/CodeNameMapStore.cs'),
    (Join-Path $root 'src/Debug/DebugMenuData.cs'),
    (Join-Path $PSScriptRoot 'DebugMenuTests.cs')
) -ReferencedAssemblies $references
[LOMSave.Probe.DebugMenuTests]::Run((Join-Path $root 'artifacts/LOMSaveProbe/LOMSaveProbe.CodeNames.yaml'))
