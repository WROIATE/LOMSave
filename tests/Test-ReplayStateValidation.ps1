$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'artifacts/LOMSaveProbe/Newtonsoft.Json.dll')
Add-Type -Path @(
    (Join-Path $root 'src/Replay/ReplayStateValidator.cs'),
    (Join-Path $root 'src/Replay/ContinuationShopGuard.cs'),
    (Join-Path $root 'src/Replay/ReplayOriginPolicy.cs'),
    (Join-Path $PSScriptRoot 'ReplayStateValidationTests.cs')
) -ReferencedAssemblies @(
    (Join-Path $root 'artifacts/LOMSaveProbe/Newtonsoft.Json.dll'),
    'System.Runtime', 'System.Collections', 'System.Linq', 'netstandard'
) -CompilerOptions '/nowarn:0649'
[LOMSave.Probe.ReplayStateValidationTests]::Run()
$story = Join-Path $root 'obj/game-update-20261015/story/ch6_10_3_break_start_01.txt'
if (!(Test-Path -LiteralPath $story)) { throw 'Temple regression requires the extracted current game script.' }
$source = [IO.File]::ReadAllText($story)
$choice = 'local choice_talk = choose(talk)'
$offset = $source.IndexOf($choice, [StringComparison]::Ordinal)
if ($offset -lt 0) { throw 'Native temple menu fixture changed.' }
$prefix = $source.Substring(0, $offset + $choice.Length)
$fungus = [Reflection.Assembly]::LoadFrom('C:/Game/Steam/steamapps/common/LegendOfMortal/Mortal_Data/Managed/Fungus.dll')
$setup = @'
luamanager = {PlayMusic = function() end, ToggleSaveButton = function() end}
runblock = function() end
runwait = function() end
characters = {LoadCharacterAsset = function() end, LoadCharacterPortrait = function() end,
 Get = function() return {} end, GetPortrait = function() return {} end,
 RestoreRotation = function() end, Focus = function() end}
stage = {show = function() end}
flowcharts = {view = {}, LoadView = function() end}
getvar = function() return {} end
menudialogs = {Talk = {}}
setmenudialog = function() end
checkpointmanager = {Condition = function(key) return key == 'Talk_Option_01' and entryFlag == 0 end}
choose = function(options) captured = options; return 1 end
'@
$scriptType = $fungus.GetType('MoonSharp.Interpreter.Script', $true)
$old = [Activator]::CreateInstance($scriptType)
[void]$old.DoString("entryFlag = 1`n" + $setup + $prefix + "`nassert(captured[4] == '~x', 'stale inn flags must reproduce missing temple option')", $null, 'temple-before.LuaScript')
$fixed = [Activator]::CreateInstance($scriptType)
$flag = [LOMSave.Probe.ReplayStateValidationTests]::TempleEntryFlag()
[void]$fixed.DoString("entryFlag = $flag`n" + $setup + $prefix + "`nassert(captured[4] == 'event+O_6_10_3_Break_01_006+0+0', 'selected target flags must reproduce original temple menu')", $null, 'temple-after.LuaScript')
'2 native temple menu / embedded Lua assertions passed (stale origin failure reproduced; selected target succeeds).'
