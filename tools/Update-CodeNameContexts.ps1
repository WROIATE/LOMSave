param(
    [string]$GameDir = 'C:\Game\Steam\steamapps\common\LegendOfMortal',
    [string]$Python = 'python',
    [string]$MapPath,
    [string]$SourcesPath,
    [string]$OutputPath,
    [string]$SourcesOutputPath,
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$MapPath) { $MapPath = Join-Path $root 'artifacts/LOMSaveProbe/LOMSaveProbe.CodeNames.yaml' }
if (!$OutputPath) { $OutputPath = Join-Path $root 'obj/code-names-context.yaml' }
if (!$ReportPath) { $ReportPath = Join-Path $root 'obj/code-names-context-report.json' }
if (!$SourcesPath) { $SourcesPath = [IO.Path]::ChangeExtension($MapPath, 'Sources' + [IO.Path]::GetExtension($MapPath)) }
if (!$SourcesOutputPath) { $SourcesOutputPath = [IO.Path]::ChangeExtension($OutputPath, 'Sources' + [IO.Path]::GetExtension($OutputPath)) }

# Parse edited YAML with the same standard parser used by the plugin. Writing
# the reviewed result/installation is a separate step; never overwrite input.
foreach ($candidateOutput in @($OutputPath, $SourcesOutputPath, $ReportPath)) {
    foreach ($candidateInput in @($MapPath, $SourcesPath)) {
        if ([IO.Path]::GetFullPath($candidateInput) -eq [IO.Path]::GetFullPath($candidateOutput)) {
            throw 'Use separate output files so existing names and evidence remain reviewable.'
        }
    }
}
if (@(@($OutputPath, $SourcesOutputPath, $ReportPath) | ForEach-Object { [IO.Path]::GetFullPath($_) } | Select-Object -Unique).Count -ne 3) {
    throw 'The runtime, sources and report outputs must use different files.'
}
Add-Type -Path (Join-Path $root 'artifacts/LOMSaveProbe/YamlDotNet.dll')
$reader = [YamlDotNet.Serialization.DeserializerBuilder]::new().WithDuplicateKeyChecking().Build()
$document = $reader.Deserialize[object]([IO.File]::ReadAllText($MapPath))
$baseline = Join-Path $root 'obj/code-names-baseline.json'
$document | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $baseline -Encoding utf8
$sourceDocument = $null
$sourceArguments = @()
if (Test-Path -LiteralPath $SourcesPath) {
    $sourceDocument = $reader.Deserialize[object]([IO.File]::ReadAllText($SourcesPath))
    $sourceBaseline = Join-Path $root 'obj/code-names-baseline-sources.json'
    $sourceDocument | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $sourceBaseline -Encoding utf8
    $sourceArguments = @('--baseline-sources-json', $sourceBaseline)
}

[void][Reflection.Assembly]::LoadFrom((Join-Path $GameDir 'BepInEx/core/Mono.Cecil.dll'))
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDir 'Mortal_Data/Managed/Mortal.Core.dll'))
$enums = @{}
foreach ($type in ($assembly.MainModule.Types | Where-Object { $_.Name -in @('GameStatType','RelationshipStatType','MonthStageType') })) {
    $values = @{}
    foreach ($field in ($type.Fields | Where-Object HasConstant)) {
        $attribute = $field.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'StringValueAttribute' } | Select-Object -First 1
        $values[[string]$field.Constant] = @{
            name = $field.Name
            key = if ($attribute) { [string]$attribute.ConstructorArguments[0].Value } else { '' }
        }
    }
    $enums[$type.Name] = $values
}
$enumFile = Join-Path $root 'obj/code-names-enums.json'
$enums | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $enumFile -Encoding utf8
$inventory = Join-Path $root 'obj/code-names-analysis.json'
& $Python (Join-Path $PSScriptRoot 'export_code_names.py') --game-dir $GameDir --output (Join-Path $root 'obj/code-names-native.yaml') --analysis-output $inventory --enums-json $enumFile
if ($LASTEXITCODE) { throw 'Static inventory export failed.' }
& $Python (Join-Path $PSScriptRoot 'summarize_code_names.py') --inventory $inventory --baseline-json $baseline @sourceArguments --output $OutputPath --sources-output $SourcesOutputPath --report $ReportPath
if ($LASTEXITCODE) { throw 'Context analysis failed.' }

# Parse the finished map and verify preservation before making it available.
$result = $reader.Deserialize[object]([IO.File]::ReadAllText($OutputPath))
$resultSources = $reader.Deserialize[object]([IO.File]::ReadAllText($SourcesOutputPath))
foreach ($scope in $document['mappings'].Keys) {
    if ($result['mappings'][$scope].Count -ne $document['mappings'][$scope].Count) { throw "Inventory changed: $scope" }
    if ($resultSources['mappings'][$scope].Count -ne $document['mappings'][$scope].Count) { throw "Sources inventory changed: $scope" }
    foreach ($code in $document['mappings'][$scope].Keys) {
        $old = $document['mappings'][$scope][$code]
        $new = $result['mappings'][$scope][$code]
        if ($new.ContainsKey('sources')) { throw "Evidence leaked into the runtime map: $scope/$code" }
        if ($old['name'] -or $scope -notin @('conditions','switches')) {
            $oldRuntime = [ordered]@{}
            foreach ($key in $old.Keys) { if ($key -ne 'sources') { $oldRuntime[$key] = $old[$key] } }
            if (($oldRuntime | ConvertTo-Json -Depth 20 -Compress) -cne ($new | ConvertTo-Json -Depth 20 -Compress)) {
                throw "Existing entry changed: $scope/$code"
            }
        }
        $evidence = [Collections.Generic.List[string]]::new()
        foreach ($source in $old['sources']) { if (!$evidence.Contains($source)) { $evidence.Add($source) } }
        if ($sourceDocument -and $sourceDocument['mappings'].ContainsKey($scope) -and $sourceDocument['mappings'][$scope].ContainsKey($code)) {
            foreach ($source in $sourceDocument['mappings'][$scope][$code]['sources']) {
                if (!$evidence.Contains($source)) { $evidence.Add($source) }
            }
        }
        $newEvidence = @($resultSources['mappings'][$scope][$code]['sources'])
        foreach ($source in $evidence) { if ($source -cnotin $newEvidence) { throw "Evidence lost: $scope/$code" } }
        if ($new['name'].Length -gt 64) { throw "Name exceeds display limit: $scope/$code" }
    }
}
'Verified: existing names and evidence preserved; runtime/source YAML files parse separately.'
