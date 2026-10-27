$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @((Join-Path $root 'src/Saves/NativeSaveApi.cs'), (Join-Path $root 'src/Replay/NativeStoryLog.cs'), (Join-Path $PSScriptRoot 'NativeStoryLogTests.cs'))
[LOMSave.Probe.NativeStoryLogTests]::Run()

# Exercise the installed game's managed history implementation without Unity,
# creating a window, loading a scene, or reading/writing a player save.
$native = [Reflection.Assembly]::LoadFrom('C:/Game/Steam/steamapps/common/LegendOfMortal/Mortal_Data/Managed/Mortal.Core.dll')
$saveType = $native.GetType('Mortal.Core.GameSave', $true)
$logType = $native.GetType('Mortal.Core.StoryLogData', $true)
[void]$logType.GetMethod('Reset').Invoke($null, @())
[void]$logType.GetMethod('Add').Invoke($null, @('N_Test_1', '', $true))
[void]$logType.GetMethod('MarkRestartPoint').Invoke($null, @())
[void]$logType.GetMethod('Add').Invoke($null, @('N_Test_2', '', $true))
$snapshot = [Activator]::CreateInstance($saveType)
[void]$logType.GetMethod('Save').Invoke($null, @($snapshot, $false))
if ($snapshot.StoryLogs.Count -ne 2) { throw 'Native complete-history semantics changed.' }
$restart = [Activator]::CreateInstance($saveType)
[void]$logType.GetMethod('Save').Invoke($null, @($restart, $true))
if ($restart.StoryLogs.Count -ne 1) { throw 'Native restart-prefix semantics changed.' }
[void]$logType.GetMethod('Load').Invoke($null, @($snapshot))
$restored = [Activator]::CreateInstance($saveType)
[void]$logType.GetMethod('Save').Invoke($null, @($restored, $false))
if ($restored.StoryLogs.Count -ne 2 -or $restored.StoryLogs[1].Key -ne 'N_Test_2') { throw 'Native history load lost or duplicated dialogue.' }
[void]$logType.GetMethod('Reset').Invoke($null, @())
'3 installed native history assertions passed (current prefix, restart prefix, load round-trip); no game launched.'
