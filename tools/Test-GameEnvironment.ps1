param([string]$GameDir = 'C:\Game\Steam\steamapps\common\LegendOfMortal')
$ErrorActionPreference = 'Stop'

function Get-NativeArchitecture([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Not a PE file: $Path" }
        [void]$stream.Seek(0x3C, [IO.SeekOrigin]::Begin)
        $offset = $reader.ReadInt32()
        [void]$stream.Seek($offset, [IO.SeekOrigin]::Begin)
        if ($reader.ReadUInt32() -ne 0x4550) { throw "Invalid PE header: $Path" }
        switch ($reader.ReadUInt16()) {
            0x014C { 'x86' }
            0x8664 { 'x64' }
            0xAA64 { 'arm64' }
            default { throw "Unrecognized native architecture: $Path" }
        }
    } finally { $reader.Dispose(); $stream.Dispose() }
}

$taskExecutable = Join-Path $GameDir 'Mortal.exe'
$taskLoader = Join-Path $GameDir 'winhttp.dll'
$taskGameArchitecture = Get-NativeArchitecture $taskExecutable
$taskLoaderArchitecture = if (Test-Path -LiteralPath $taskLoader) { Get-NativeArchitecture $taskLoader } else { $null }
$taskPreloader = Test-Path -LiteralPath (Join-Path $GameDir 'BepInEx/core/BepInEx.Unity.Mono.Preloader.dll')
$taskCompatible = $taskGameArchitecture -eq $taskLoaderArchitecture -and $taskPreloader
if (!$taskCompatible) {
    Write-Warning "Game architecture: $taskGameArchitecture; winhttp.dll: $taskLoaderArchitecture; Mono preloader present: $taskPreloader. Install the matching BepInEx 6 Unity.Mono Windows package before testing plugins. Compilation alone does not verify the native loader."
}
[pscustomobject]@{
    game = $taskExecutable
    gameArchitecture = $taskGameArchitecture
    loaderArchitecture = $taskLoaderArchitecture
    monoPreloaderPresent = $taskPreloader
    nativeLoaderCompatible = $taskCompatible
}
