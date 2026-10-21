$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$references = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName) + @((Join-Path $PSHOME 'Newtonsoft.Json.dll'))
Add-Type -Path @(
    (Join-Path $root 'src/Saves/LocalJsonFile.cs'),
    (Join-Path $root 'src/Debug/TrainingLimitSettings.cs'),
    (Join-Path $root 'src/Debug/TrainingPointLimits.cs'),
    (Join-Path $PSScriptRoot 'TrainingLimitTests.cs')
) -ReferencedAssemblies $references -CompilerOptions '/nowarn:1701,0649'
[LOMSave.Probe.TrainingLimitTests]::Run((Join-Path $root ('obj/training-limit-tests/' + [Guid]::NewGuid().ToString('N'))))

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
} finally { $native.Dispose() }
