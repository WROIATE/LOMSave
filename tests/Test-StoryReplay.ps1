$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'artifacts/LOMSaveProbe/Newtonsoft.Json.dll')
Add-Type -Path @(
    (Join-Path $root 'src/Replay/StoryCommandTrace.cs'),
    (Join-Path $root 'src/Replay/StoryReplayJournal.cs'),
    (Join-Path $root 'src/Replay/DiceRollTrace.cs'),
    (Join-Path $root 'src/Replay/ReplayMissionGuard.cs'),
    (Join-Path $PSScriptRoot 'StoryReplayTests.cs')
) -ReferencedAssemblies @((Join-Path $root 'artifacts/LOMSaveProbe/Newtonsoft.Json.dll'), 'System.Runtime', 'System.Collections', 'System.Linq', 'System.Security.Cryptography', 'netstandard')
[LOMSave.Probe.StoryReplayTests]::Run()
$fungus = [Reflection.Assembly]::LoadFrom('C:/Game/Steam/steamapps/common/LegendOfMortal/Mortal_Data/Managed/Fungus.dll')
$vm = [Activator]::CreateInstance($fungus.GetType('MoonSharp.Interpreter.Script', $true))
$setup = @'
mode = 'skip'
originalCalls = 0
say = function() originalCalls = originalCalls + 1; return 9 end
json = {serialize = function() return 'args' end}
randomCalls = 0
math.random = function() randomCalls = randomCalls + 1; return 13 end
luamanager = {GetStoryText = function(key)
 if string.find(key, 'random|end|', 1, true) then return mode == 'abort' and 'abort' or '17' end
 if string.find(key, '|begin|', 1, true) then return '1|' .. mode end
 return ''
end}
'@
[void]$vm.DoString($setup + "`n" + [LOMSave.Probe.StoryReplayTests]::Installer(), $null, 'production-trace.LuaScript')
$checks = @'
local co = coroutine.create(function() say('a'); say('b'); return 42 end)
assert(coroutine.resume(co) and coroutine.status(co) == 'suspended', 'first skipped line must yield')
assert(coroutine.resume(co) and coroutine.status(co) == 'suspended', 'second skipped line must yield')
local ok, result = coroutine.resume(co)
assert(ok and result == 42 and coroutine.status(co) == 'dead' and originalCalls == 0, 'skip must avoid dialogue UI')
mode = 'continue'
assert(say('normal') == 9 and originalCalls == 1, 'normal dialogue remains native')
assert(math.random(80) == 17 and randomCalls == 1, 'recorded random result must still advance native RNG')
mode = 'abort'
assert(not pcall(math.random, 80) and randomCalls == 2, 'random mismatch must stop Lua')
'@
[void]$vm.DoString($checks, $null, 'trace-regression.LuaScript')
'6 embedded Lua replay scheduling and RNG assertions passed.'
