param(
    [string]$GameDir = 'C:/Game/Steam/steamapps/common/LegendOfMortal',
    [string]$PluginPath = 'obj/feature-update/LOMSaveProbe.dll'
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameDir 'BepInEx/core/Mono.Cecil.dll')
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
foreach ($directory in @("$GameDir/Mortal_Data/Managed", "$GameDir/BepInEx/core", "$PSScriptRoot/../artifacts/LOMSaveProbe")) { $resolver.AddSearchDirectory($directory) }
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$plugin = [Mono.Cecil.AssemblyDefinition]::ReadAssembly([IO.Path]::GetFullPath($PluginPath), $parameters)
$problems = [Collections.Generic.List[string]]::new()
$referenceCount = 0
$patchCount = 0
$reflectionCount = 0
try {
    # A valid Harmony target is useless if its containing class is never
    # registered. Verify PatchAll(Type) registrations in the built assembly.
    $registeredPatchTypes = [Collections.Generic.HashSet[string]]::new()
    foreach ($type in $plugin.MainModule.GetTypes()) {
        foreach ($method in $type.Methods) {
            if (!$method.HasBody) { continue }
            $instructions = $method.Body.Instructions
            for ($index = 2; $index -lt $instructions.Count; $index++) {
                if ([string]$instructions[$index].Operand -match 'HarmonyLib\.Harmony::PatchAll\(System.Type\)' -and
                    $instructions[$index - 2].OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldtoken) {
                    [void]$registeredPatchTypes.Add($instructions[$index - 2].Operand.FullName)
                }
            }
        }
    }
    foreach ($reference in $plugin.MainModule.GetMemberReferences()) {
        if ($reference.DeclaringType.Scope.Name -notmatch '^(Mortal\.|Fungus|UnityEngine\.)') { continue }
        $referenceCount++
        try { if (!$reference.Resolve()) { $problems.Add("Unresolved: $reference") } }
        catch { $problems.Add("Unresolved: $reference : $_") }
    }
    foreach ($type in $plugin.MainModule.GetTypes()) {
        $hasPatches = @($type.Methods | Where-Object {
            @($_.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' }).Count -gt 0
        }).Count -gt 0
        if ($hasPatches -and !$registeredPatchTypes.Contains($type.FullName)) {
            $problems.Add("Harmony class is not registered: $($type.FullName)")
        }
        foreach ($method in $type.Methods) {
            # Harmony also discovers callbacks by convention, even without an
            # attribute. A business method named Prepare can silently disable
            # every patch in its class when Harmony calls it with nulls.
            if ($hasPatches -and $method.Name -in @('Prepare','Cleanup','TargetMethod','TargetMethods') -and
                !@($method.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq ('HarmonyLib.Harmony' + $method.Name) }).Count) {
                $problems.Add("Implicit Harmony lifecycle callback requires an explicit attribute or a different name: $method")
            }
            foreach ($attribute in $method.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' }) {
                $patchCount++
                $targetType = $null; $targetName = $null; $argumentTypes = $null; $methodType = 0
                foreach ($argument in $attribute.ConstructorArguments) {
                    switch ($argument.Type.FullName) {
                        'System.Type' { $targetType = $argument.Value }
                        'System.String' { $targetName = [string]$argument.Value }
                        'System.Type[]' { $argumentTypes = @($argument.Value | ForEach-Object { $_.Value.FullName }) }
                        'HarmonyLib.MethodType' { $methodType = [int]$argument.Value }
                    }
                }
                if (!$targetType -or !$targetName) { $problems.Add("Incomplete patch: $method"); continue }
                $definition = $targetType.Resolve()
                $targetMethods = @()
                while ($definition -and !$targetMethods.Count) {
                    $targetMethods = @($definition.Methods | Where-Object Name -eq $targetName)
                    if ($null -ne $argumentTypes) { $targetMethods = @($targetMethods | Where-Object { ($_.Parameters.ParameterType.FullName -join ',') -eq ($argumentTypes -join ',') }) }
                    if (!$targetMethods.Count) { $definition = if ($definition.BaseType) { $definition.BaseType.Resolve() } else { $null } }
                }
                if ($targetMethods.Count -ne 1) { $problems.Add("Missing/ambiguous target: $method -> $targetType/$targetName"); continue }
                if ($methodType -eq 5) {
                    # Harmony's Enumerator patches the generated MoveNext,
                    # not the public method that constructs the iterator.
                    $iterator = @($targetMethods[0].Body.Instructions | Where-Object { $_.OpCode.Code -eq 'Newobj' } |
                        ForEach-Object { $_.Operand.DeclaringType.Resolve() } |
                        Where-Object { $_.DeclaringType.FullName -eq $definition.FullName })
                    $targetMethods = @($iterator.Methods | Where-Object Name -eq 'MoveNext')
                    if ($targetMethods.Count -ne 1) { $problems.Add("Missing/ambiguous iterator target: $method"); continue }
                }
                foreach ($parameter in $method.Parameters) {
                    if (@($method.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyTranspiler' }).Count -gt 0 -and
                        $parameter.ParameterType.FullName -eq 'System.Collections.Generic.IEnumerable`1<HarmonyLib.CodeInstruction>') { continue }
                    if ($parameter.Name -match '^__(\d+)$') {
                        $index = [int]$Matches[1]
                        if ($index -ge $targetMethods[0].Parameters.Count) { $problems.Add("Argument index missing: $method/$parameter") }
                    } elseif ($parameter.Name -notmatch '^__' -and $parameter.Name -notin $targetMethods[0].Parameters.Name) {
                        $problems.Add("Named argument missing: $method/$parameter")
                    }
                }
            }
        }
    }
    $contracts = @(
        @{ Assembly = 'Mortal.Core'; Type = 'Mortal.Core.SaveSystem'; Methods = @('AutoSaveData','GetSaveDataFile') },
        @{ Assembly = 'Mortal.Core'; Type = 'Mortal.Core.LoadGamePanel'; Fields = @('_autoSaveSlot') },
        @{ Assembly = 'Mortal.Core'; Type = 'Mortal.Core.AutoSaveSlotPanel'; Fields = @('_slot','_slotText') },
        @{ Assembly = 'Mortal.Core'; Type = 'Mortal.Core.SoundManager'; Fields = @('_currentMusic','_currentEnvSound','_rtpcBackgroundMusic','_rtpcEnvSound','_rtpcMusicLowpass') },
        @{ Assembly = 'Mortal.Core'; Type = 'Mortal.Core.GameDateTimeText'; Fields = @('_text') },
        @{ Assembly = 'Mortal.Core'; Type = 'Mortal.Core.PlayerStatusPanel'; Fields = @('_testPanel') },
        @{ Assembly = 'Mortal.Core'; Type = 'Mortal.Core.PlayerStatManagerData'; Fields = @('_startStoryScript') },
        @{ Assembly = 'Mortal.Core'; Type = 'Mortal.Core.TestGameStatPanel'; Fields = @('_stat','_nameText') },
        @{ Assembly = 'Mortal.Core'; Type = 'Mortal.Core.TestFlagStatPanel'; Fields = @('_stat','_nameText','_valueText','_textTip','_tip') },
        @{ Assembly = 'Mortal.Core'; Type = 'Mortal.Core.MartialPanel'; Fields = @('_maxDataConvert') },
        @{ Assembly = 'Mortal.Core'; Type = 'Mortal.Core.UpgradePanel'; Fields = @('_maxDataConvert','_pointData') },
        @{ Assembly = 'Mortal.Story'; Type = 'Mortal.Story.CheckPointManager'; Fields = @('_position') },
        @{ Assembly = 'Mortal.Story'; Type = 'Mortal.Story.StoryManager'; Fields = @('_logButton','_logOpen','_panelOpen'); Properties = @('IsStoryPause') },
        @{ Assembly = 'Mortal.Story'; Type = 'Mortal.Story.CharacterPlaceholder'; Fields = @('_characters') },
        @{ Assembly = 'Mortal.Story'; Type = 'Mortal.Story.StoryCharacterController'; Fields = @('_dicPortrait') },
        @{ Assembly = 'Mortal.Story'; Type = 'Mortal.Story.DiceMenuDialog'; Fields = @('_defaultDiceMax','_currentRandomValue','_resultCount','_resultSelection','_header','_additions') },
        @{ Assembly = 'Mortal.Pinball'; Type = 'Mortal.Pinball.PinballManager'; Fields = @('_isBroken','_spring','_currentScore','_totalScore'); Methods = @('DisableMarblePhysicsBehaviour','SetScoreTriggerActive','ExitScene') },
        @{ Assembly = 'Mortal.Battle'; Type = 'Mortal.Battle.GameLevelManager'; Fields = @('_progressLevelData_01','_progressLevelData_02','_pausePanel') },
        @{ Assembly = 'Mortal.Battle'; Type = 'Mortal.Battle.ReadyPanel'; Fields = @('_startButton') },
        @{ Assembly = 'Mortal.Battle'; Type = 'Mortal.Battle.PausePanel'; Fields = @('_testWinButton','_giveupButton','_interactableButtons') },
        @{ Assembly = 'Mortal.Battle'; Type = 'Mortal.Battle.ProgressLevelData'; Fields = @('_max','_flagData') }
    )
    foreach ($contract in $contracts) {
        $assemblyRef = [Mono.Cecil.AssemblyNameReference]::new($contract.Assembly, [Version]'0.0.0.0')
        $definition = $resolver.Resolve($assemblyRef).MainModule.GetType($contract.Type)
        foreach ($kind in @('Fields','Methods','Properties')) {
            foreach ($name in $contract[$kind]) {
                $reflectionCount++
                if (!$definition -or $name -notin $definition.$kind.Name) { $problems.Add("Missing reflection member: $($contract.Type)/$name") }
            }
        }
    }
    # Traverse.Method(name) selects an empty parameter list; GetValue(arguments)
    # does not participate in overload selection. Catch this before deployment.
    foreach ($source in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../src') -Filter '*.cs' -Recurse) {
        if ((Get-Content -LiteralPath $source.FullName -Raw) -match '\.Method\("[^"]+"\)\.GetValue(?:<[^>]+>)?\((?!\s*\))') {
            $problems.Add("Reflection call needs explicit argument types: $($source.FullName)")
        }
    }
    if ($problems.Count) { $problems | Write-Output; throw 'Plugin API compatibility check failed.' }
    "$referenceCount native references, $patchCount Harmony targets and $reflectionCount reflection members resolved; $($registeredPatchTypes.Count) patch classes registered."
} finally { $plugin.Dispose(); $resolver.Dispose() }
