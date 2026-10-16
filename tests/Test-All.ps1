param([string]$StoryDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$StoryDirectory) {
    $StoryDirectory = Join-Path $root 'obj/game-update-20261015/story'
    if (!(Test-Path -LiteralPath $StoryDirectory)) { $StoryDirectory = Join-Path $root 'obj/story' }
}
$taskTemp = Join-Path $root ('obj/regression-temp/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($taskTemp)
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
try {
    $env:TEMP = $taskTemp
    $env:TMP = $taskTemp
    $scripts = @('ActionAutoSave','StorySaveFiles','ReplayRuntime','DialogueAndImpact','BuildingLongPress',
        'CodeNames','PreviewFont','AudioSceneState','FeatureData','CalendarRuntime','RecallShortcut','DebugMenu')
    foreach ($name in $scripts) {
        $arguments = @('-NoProfile', '-File', (Join-Path $PSScriptRoot "Test-$name.ps1"))
        if ($name -eq 'DialogueAndImpact') { $arguments += @('-StoryDirectory', $StoryDirectory) }
        & (Join-Path $PSHOME 'pwsh.exe') @arguments
        if ($LASTEXITCODE -ne 0) { throw "$name regression failed ($LASTEXITCODE)." }
    }
    "$($scripts.Count) regression suites passed."
} finally { $env:TEMP = $previousTemp; $env:TMP = $previousTmp }
