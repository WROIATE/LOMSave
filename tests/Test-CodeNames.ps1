$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$yamlLibrary = Join-Path $root 'artifacts/LOMSaveProbe/YamlDotNet.dll'
Add-Type -Path $yamlLibrary
$references = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName) + @($yamlLibrary)
Add-Type -Path @(
    (Join-Path $root 'CheckpointNameRules.cs'),
    (Join-Path $root 'CodeNameMapStore.cs'),
    (Join-Path $PSScriptRoot 'CodeNameMappingTests.cs')
) -ReferencedAssemblies $references
[LOMSave.Probe.CodeNameMappingTests]::Run(
    (Join-Path $root 'artifacts/LOMSaveProbe/LOMSaveProbe.CodeNames.yaml'),
    (Join-Path $root 'obj/code-name-tests'))
