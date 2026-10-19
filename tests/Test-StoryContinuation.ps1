param([switch]$Corpus, [string]$StoryDirectory = 'obj/game-update-20261015/story')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @((Join-Path $root 'src/Replay/StoryContinuationCompiler.cs'), (Join-Path $PSScriptRoot 'StoryContinuationTests.cs'))
[LOMSave.Probe.StoryContinuationTests]::Run()
$fungus = [Reflection.Assembly]::LoadFrom('C:/Game/Steam/steamapps/common/LegendOfMortal/Mortal_Data/Managed/Fungus.dll')
$vmType = $fungus.GetType('MoonSharp.Interpreter.Script', $true)
$setup = @'
money = 100; attributes = 0; flags = 1; chosen = 1
function decode(v)
 if v.kind ~= 'table' then return v.value end
 local t={}; for _,entry in ipairs(v.entries) do t[decode(entry.key)]=decode(entry.value) end; return t
end
json = {serialize = function(values) captured={}; for k,v in pairs(values) do captured[k]=decode(v) end; return '{}' end}
luamanager = {GetStoryText = function(key)
 if key == '__LOM_CONT_READY__' then return 'ready' end
 if string.find(key, '__LOM_CONT_POINT__', 1, true) == 1 then
   point = tonumber(string.match(key, '__LOM_CONT_POINT__(%d+)'))
 end
 return ''
end}
say = function(text) shown = text; coroutine.yield(text) end
choose = function(options) coroutine.yield('choose'); return chosen end
take = function(n) money = money - n end
award = function(n) attributes = attributes + n end
use = function(n) attributes = attributes + n end
function literal(v)
 if type(v) == 'table' then local s = '{'; for k,x in pairs(v) do s=s..'['..literal(k)..']='..literal(x)..',' end; return s..'}' end
 if type(v) == 'string' then return string.format('%q',v) end
 return tostring(v)
end
'@
$fixture = @'
local result = flags
take(20)
var1 = 13
if result == 1 then
 local result = 7
 award(3)
 say('saved')
 use(result)
 local options = {}
 options[1] = 'stay'
 options[2] = 'leave'
 local choice = choose(options)
 if choice == 1 then
  take(9)
  say('stay')
 else
  award(40)
  say('leave')
 end
else
 take(90)
 say('wrong branch')
end
use(result)
use(var1)
say('finished')
'@
$vm = [Activator]::CreateInstance($vmType)
$compiled = [LOMSave.Probe.StoryContinuationTests]::Compile($fixture)
[void]$vm.DoString($setup + "`nco = coroutine.create(function()`n" + $compiled + "`nend)`nassert(coroutine.resume(co)); assert(shown == 'saved' and money == 80 and attributes == 3)", $null, 'original')
$point = [int]$vm.DoString('return point', $null, 'point').Number
$locals = $vm.DoString('return literal(captured)', $null, 'snapshot').String
$resumed = [LOMSave.Probe.StoryContinuationTests]::Resume($fixture, $point)
$fresh = [Activator]::CreateInstance($vmType)
$before = $setup + "`nmoney=80; attributes=3; flags=2; __saved=$locals`nco=coroutine.create(function()`n" + $resumed + "`nend)`n"
[void]$fresh.DoString($before + "assert(coroutine.resume(co)); assert(shown == 'saved' and money == 80 and attributes == 3, 'restore replayed effects')", $null, 'restore')
[void]$fresh.DoString("assert(coroutine.resume(co)); assert(attributes==10 and money==80, 'local or branch lost')", $null, 'continue')
$choicePoint = [int]$fresh.DoString('return point', $null, 'point').Number
$choiceLocals = $fresh.DoString('return literal(captured)', $null, 'snapshot').String
$choiceResume = [LOMSave.Probe.StoryContinuationTests]::Resume($fixture, $choicePoint)
$branch = [Activator]::CreateInstance($vmType)
[void]$branch.DoString($setup + "`nmoney=80; attributes=10; __saved=$choiceLocals`nco=coroutine.create(function()`n" + $choiceResume + "`nend)`nassert(coroutine.resume(co)); chosen=2; assert(coroutine.resume(co)); assert(shown=='leave' and attributes==50 and money==80)", $null, 'choice-restore')
[void]$branch.DoString("assert(coroutine.resume(co)); assert(shown=='finished' and attributes==64 and money==80, 'outer scope, global or join lost')", $null, 'join')
[void]$fresh.DoString("assert(coroutine.resume(co)); assert(shown=='stay' and money==71 and attributes==10); assert(coroutine.resume(co)); assert(shown=='finished' and attributes==24 and money==71)", $null, 'original-choice')
'Continuation tests passed: excluded prior effects, preserved shadowed locals, original branch despite changed flags, choice snapshot, alternate choice and parent continuation.'
$actorSetup = @'
actors = {}; actorLoads = {}; hidden = {}; shown = ''; selected = 1
json = {serialize = function(values) captured = values; return '{}' end}
luamanager = {GetStoryText = function(key)
 if key == '__LOM_CONT_READY__' then return 'ready' end
 local actor = string.match(key, '^__LOM_CONT_ACTOR__(.+)$')
 if actor then return actors[actor] and 'ready' or 'missing' end
 local savedPoint = string.match(key, '^__LOM_CONT_POINT__(%d+)|')
 if savedPoint then point = tonumber(savedPoint) end
 return ''
end}
characters = {
 LoadCharacterAsset = function(key)
  if not actors[key] then actors[key] = {key=key}; actorLoads[key] = (actorLoads[key] or 0) + 1 end
 end,
 Get = function(key) return actors[key] end
}
runwait = function(value) return value end
stage = {
 show = function(options) assert(options.character, 'show requires loaded actor'); options.character.mood = true end,
 hide = function(options)
  if options.character then
   assert(options.character.mood, 'HideMood: actor was loaded without ever being shown')
   hidden[options.character.key] = true
  end
 end
}
say = function(text) shown = text; coroutine.yield(text) end
'@
$actorFixture = @'
runwait(characters.LoadCharacterAsset('player'))
stage.show{character=characters.Get('player')}
runwait(characters.LoadCharacterAsset('inherited'))
if selected == 1 then
 runwait(characters.LoadCharacterAsset('girl5'))
 stage.show{character=characters.Get('girl5')}
else
 runwait(characters.LoadCharacterAsset('girl7'))
 stage.show{character=characters.Get('girl7')}
end
say('before cleanup')
stage.hide{character=characters.Get('player')}
stage.hide{character=characters.Get('girl7')}
stage.hide{character=characters.Get('girl5')}
stage.show{character=characters.Get('inherited')}
stage.hide{character=characters.Get('inherited')}
say('next scene')
'@
foreach ($mode in @('native', 'instrumented', 'legacy-inherited-context')) {
 $actorVm = [Activator]::CreateInstance($vmType)
 $actorCode = if ($mode -eq 'native') { $actorFixture } elseif ($mode -eq 'instrumented') { [LOMSave.Probe.StoryContinuationTests]::Compile($actorFixture) } else { [LOMSave.Probe.StoryContinuationTests]::CompileWithInheritedContext($actorFixture) }
 [void]$actorVm.DoString($actorSetup + "`nco=coroutine.create(function()`n" + $actorCode + "`nend)`nassert(coroutine.resume(co)); assert(shown=='before cleanup'); assert(coroutine.resume(co)); assert(shown=='next scene'); assert(actors.girl7==nil and actorLoads.girl7==nil, 'unused branch actor was loaded'); assert(hidden.player and hidden.girl5 and hidden.inherited)", $null, ('actor-cleanup-' + $mode))
}
# The second say is now active; resume the first checkpoint to exercise cleanup.
$actorPoint = [LOMSave.Probe.StoryContinuationTests]::Points($actorFixture)[0]
$actorResume = [LOMSave.Probe.StoryContinuationTests]::Resume($actorFixture, $actorPoint)
$actorVm = [Activator]::CreateInstance($vmType)
[void]$actorVm.DoString($actorSetup + "`n__saved={}; actors.player={key='player',mood=true}; actors.girl5={key='girl5',mood=true}; co=coroutine.create(function()`n" + $actorResume + "`nend)`nassert(coroutine.resume(co)); assert(shown=='before cleanup'); assert(coroutine.resume(co)); assert(shown=='next scene'); assert(actors.girl7==nil and actorLoads.girl7==nil, 'restore loaded cleanup-only actor'); assert(actorLoads.inherited==1 and hidden.inherited, 'restore lost required inherited actor')", $null, 'actor-cleanup-resume')
'Actor lifecycle tests passed: native and instrumented cleanup preserve absent branch actors; direct resume loads only required presentation actors.'
if ($Corpus) {
 $clock=[Diagnostics.Stopwatch]::StartNew(); $count=0; $points=0; $slow=@(); $failed=@()
 foreach ($file in Get-ChildItem -LiteralPath $StoryDirectory -Filter '*.txt' -File) {
  if ($file.Name -eq 'names.txt' -or $file.Name -eq 'UnityTypes.txt') { continue }
  $source=[IO.File]::ReadAllText($file.FullName)
  try {
   $timer=[Diagnostics.Stopwatch]::StartNew()
   $code=[LOMSave.Probe.StoryContinuationTests]::Compile($source)
   $ms=$timer.Elapsed.TotalMilliseconds
   $engine=[Activator]::CreateInstance($vmType)
   [void]$engine.LoadString($code, $null, $file.Name)
   $ids=[LOMSave.Probe.StoryContinuationTests]::Points($source)
   $points+=$ids.Count
   if ($ids.Count) {
    foreach ($id in @($ids[0], $ids[[int]($ids.Count/2)], $ids[-1]) | Select-Object -Unique) {
     [void]$engine.LoadString([LOMSave.Probe.StoryContinuationTests]::Resume($source,$id),$null,$file.Name+'-resume')
    }
   }
   $count++; if($ms -gt 100) { $slow += [pscustomobject]@{script=$file.Name;ms=[math]::Round($ms,1)} }
  } catch { $failed += [pscustomobject]@{script=$file.Name;error=$_.Exception.Message} }
 }
 [pscustomobject]@{scripts=$count;points=$points;seconds=$clock.Elapsed.TotalSeconds;slow=$slow;failed=$failed} | ConvertTo-Json -Depth 4
 if ($failed.Count) { throw 'Story corpus compilation failed' }
}
