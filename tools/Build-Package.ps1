param([string]$OutputDirectory = 'obj/release')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskSource = Join-Path $taskRoot 'artifacts/LOMSaveProbe'
$taskVersionMatch = [regex]::Match(
    [IO.File]::ReadAllText((Join-Path $taskRoot 'src/Plugin/LOMSaveProbe.cs')),
    'public const string PluginVersion = "(?<version>\d+\.\d+\.\d+)";')
if (!$taskVersionMatch.Success) { throw 'Cannot read plugin version.' }
$taskVersion = $taskVersionMatch.Groups['version'].Value
$taskOutput = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else { [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory)) }
[void][IO.Directory]::CreateDirectory($taskOutput)
$taskZipName = "LOMSave-enhanced-v$taskVersion.zip"
$taskZip = Join-Path $taskOutput $taskZipName
$taskChecksum = Join-Path $taskOutput 'SHA256SUMS.txt'
if (Test-Path -LiteralPath $taskZip) { throw "Package already exists: $taskZip" }
$taskEntries = [ordered]@{
    'BepInEx/plugins/LOMSaveProbe.dll' = 'LOMSaveProbe.dll'
    'BepInEx/plugins/LOMSaveProbe.StoryEvents.json' = 'LOMSaveProbe.StoryEvents.json'
    'BepInEx/plugins/Newtonsoft.Json.dll' = 'Newtonsoft.Json.dll'
    'BepInEx/plugins/YamlDotNet.dll' = 'YamlDotNet.dll'
    'BepInEx/config/LOMSaveProbe.CodeNames.yaml' = 'LOMSaveProbe.CodeNames.yaml'
    'LICENSES/Newtonsoft.Json.LICENSE.txt' = 'Newtonsoft.Json.LICENSE.txt'
    'LICENSES/YamlDotNet.LICENSE.txt' = 'YamlDotNet.LICENSE.txt'
    'README.md' = 'README.md'
}
foreach ($taskFile in $taskEntries.Values) {
    if (!(Test-Path -LiteralPath (Join-Path $taskSource $taskFile) -PathType Leaf)) {
        throw "Required release file missing: $taskFile"
    }
}
$taskArchive = [IO.Compression.ZipFile]::Open($taskZip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskEntryName in $taskEntries.Keys) {
        $taskEntry = $taskArchive.CreateEntry($taskEntryName, [IO.Compression.CompressionLevel]::Optimal)
        $taskEntry.LastWriteTime = [DateTimeOffset]'2000-01-01T00:00:00Z'
        $taskEntryStream = $taskEntry.Open()
        $taskFileStream = [IO.File]::OpenRead((Join-Path $taskSource $taskEntries[$taskEntryName]))
        try { $taskFileStream.CopyTo($taskEntryStream) }
        finally { $taskFileStream.Dispose(); $taskEntryStream.Dispose() }
    }
} finally { $taskArchive.Dispose() }
$taskHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($taskChecksum, "$taskHash  $taskZipName`n", [Text.UTF8Encoding]::new($false))
[pscustomobject]@{version=$taskVersion;package=$taskZip;sha256=$taskHash;checksum=$taskChecksum;entries=$taskEntries.Count}
