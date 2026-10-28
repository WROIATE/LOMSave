$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$references = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName) + @((Join-Path $PSHOME 'Newtonsoft.Json.dll'))
Add-Type -Path @(
    (Join-Path $root 'src/Saves/LocalJsonFile.cs'),
    (Join-Path $root 'src/Debug/TrainingLimitSettings.cs'),
    (Join-Path $root 'src/Debug/TrainingPointLimits.cs'),
    (Join-Path $root 'src/Debug/TrainingLimitFormulas.cs'),
    (Join-Path $root 'src/Events/StoryEventIndexData.cs'),
    (Join-Path $PSScriptRoot 'TrainingLimitTests.cs')
) -ReferencedAssemblies $references -CompilerOptions '/nowarn:1701,0649'
[LOMSave.Probe.TrainingLimitTests]::Run((Join-Path $root ('obj/training-limit-tests/' + [Guid]::NewGuid().ToString('N'))), (Join-Path $root 'artifacts/LOMSaveProbe/LOMSaveProbe.StoryEvents.json'))

# Verify both coroutine initialization sites and both count labels against the
# installed game's actual IL, rather than assuming a compiler-generated name.
Add-Type -Path 'C:/Game/Steam/steamapps/common/LegendOfMortal/BepInEx/core/Mono.Cecil.dll'
$native = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:/Game/Steam/steamapps/common/LegendOfMortal/Mortal_Data/Managed/Mortal.Core.dll')
try {
    foreach ($typeName in @('MartialPanel', 'UpgradePanel')) {
        $owner = $native.MainModule.Types | Where-Object Name -eq $typeName
        $open = $owner.NestedTypes | Where-Object Name -like '<Open>*'
        $targets = @(
            ($open.Methods | Where-Object Name -eq 'MoveNext'),
            ($owner.Methods | Where-Object Name -eq $(if ($typeName -eq 'MartialPanel') { 'UpdateMartialPointInfo' } else { 'UpdateStatus' }))
        )
        if ($targets.Count -ne 2) { throw 'Missing native cap initialization/display target.' }
        foreach ($target in $targets) {
            $matches = 0
            $code = $target.Body.Instructions
            for ($i = 0; $i + 1 -lt $code.Count; $i++) {
                if ($code[$i].OpCode.Code -eq 'Ldfld' -and $code[$i].Operand.Name -eq '_maxDataConvert' -and
                    $code[$i].Operand.DeclaringType.FullName -eq $owner.FullName -and $code[$i + 1].OpCode.Code -eq 'Callvirt' -and
                    $code[$i + 1].Operand.FullName -eq 'System.Single Mortal.Core.StatConvertCollectionData::GetSummary()') { $matches++ }
            }
            if ($matches -ne 1) { throw "Unexpected native limit IL: $target ($matches occurrences)." }
        }
    }
    '4 native training initialization/display sites matched.'
    $targets = @{
        MartialPanel = @('CanUseLearnButton','HandleLearnNextItem')
        UpgradePanel = @('UpgradeButtonClick','NextLevelButtonClick','UpdateButton','HandleUpgradeNextItem')
    }
    $count = 0
    foreach ($typeName in $targets.Keys) {
        $owner = $native.MainModule.Types | Where-Object Name -eq $typeName
        foreach ($method in $targets[$typeName]) {
            $target = @($owner.Methods | Where-Object Name -eq $method)
            if ($target.Count -ne 1) { throw "Missing/ambiguous native budget check: $typeName.$method" }
            $reads = @($target[0].Body.Instructions | Where-Object { $_.OpCode.Code -eq 'Ldfld' -and $_.Operand.Name -eq '_currentCount' -and $_.Operand.DeclaringType.FullName -eq $owner.FullName })
            if ($reads.Count -ne 1) { throw "Native budget-check pattern changed: $typeName.$method" }
            $count++
        }
        $spend = @($owner.NestedTypes | Where-Object Name -like '<StartHandleUpgarde>*')
        $body = ($spend[0].Methods | Where-Object Name -eq 'MoveNext').Body.Instructions
        if (@($body | Where-Object { $_.OpCode.Code -eq 'Stfld' -and $_.Operand.Name -eq '_currentCount' -and $_.Previous.OpCode.Code -eq 'Sub' }).Count -ne 1) { throw "Native spending no longer decrements the original counter: $typeName" }
    }
    "$count native budget-check sites matched; both original spending counters remain separate from patched methods."
} finally { $native.Dispose() }
