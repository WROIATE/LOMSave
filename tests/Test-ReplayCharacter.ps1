param([string]$StoryDirectory, [string]$GameDir = 'C:/Game/Steam/steamapps/common/LegendOfMortal')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @(
    (Join-Path $root 'src/Replay/ReplayCharacterContext.cs'),
    (Join-Path $root 'src/Replay/StoryReplayJournal.cs'),
    (Join-Path $PSScriptRoot 'ReplayCharacterTests.cs')
) -CompilerOptions '/nowarn:0649'
[LOMSave.Probe.ReplayCharacterTests]::Run()
if (!$StoryDirectory) { $StoryDirectory = Join-Path $root 'obj/game-update-20261015/story' }
$source = [IO.File]::ReadAllText((Join-Path $StoryDirectory 'ch6_10_1_part_2.txt'))
$missingActor = [regex]::Match($source, 'stage\.show\{character=characters\.Get\("player"\)[^\r\n]*')
if (!$missingActor.Success) { throw 'Native continuation fixture no longer has the reported inherited actor use.' }
$prefix = $source.Substring(0, $missingActor.Index + $missingActor.Length)
# Run the real current game prefix in the game's embedded MoonSharp VM. Only
# Unity/resource boundaries are replaced; missing actors fail at stage.show.
$fungus = [Reflection.Assembly]::LoadFrom((Join-Path $GameDir 'Mortal_Data/Managed/Fungus.dll'))
$scriptType = $fungus.GetType('MoonSharp.Interpreter.Script', $true)
$setup = @'
local loaded = {}
characters = {
 LoadCharacterAsset = function(key) return key end,
 LoadCharacterPortrait = function(key, portrait) assert(loaded[key], 'portrait actor missing: '..key) end,
 Get = function(key) return loaded[key] end,
 GetPortrait = function(key, portrait) return portrait end,
 RestoreRotation = function() end, ResetRotation = function() end, Focus = function() end
}
runwait = function(task) if task then loaded[task] = {key=task} end end
stage = {show = function(options) assert(options.character, 'missing actor: player') end}
flowcharts = {view = {}, LoadView = function() end}
getvar = function() return {} end
runblock = function() end
transitionblack = {TransitionOut = function() end}
wait = function() end
'@
$broken = [Activator]::CreateInstance($scriptType)
$failed = $false
try { [void]$broken.DoString($setup + "`n" + $prefix, $null, 'before-fix.LuaScript') }
catch { if ($_.Exception.ToString() -notlike '*missing actor: player*') { throw }; $failed = $true }
if (!$failed) { throw 'Unpatched native script did not reproduce the missing player actor.' }
$fixed = [Activator]::CreateInstance($scriptType)
$prepared = [LOMSave.Probe.ReplayCharacterTests]::Prepare($prefix, $true)
[void]$fixed.DoString($setup + "`n" + $prepared, $null, 'after-fix.LuaScript')
'2 native continuation / embedded Lua assertions passed (failure reproduced; prepared script succeeds).'
