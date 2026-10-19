param(
    [string]$GameDir = 'C:/Game/Steam/steamapps/common/LegendOfMortal',
    [string]$OutputDirectory = 'obj/release-plugin'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# Compile against the game's Mono assemblies, never PowerShell's CoreCLR BCL.
# Roslyn ships with PowerShell, so a separate .NET SDK is not required.
Add-Type 'internal sealed class LOMSaveCompilerBootstrap { }'
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'LOMSaveProbe.csproj') -Raw
$parse = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion(
    [Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp7_3)
$trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($item in $project.Project.ItemGroup.Compile) {
    $file = Join-Path $root $item.Include
    $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        [IO.File]::ReadAllText($file), $parse, [string]$item.Include, [Text.Encoding]::UTF8))
}
$paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($item in $project.Project.ItemGroup.Reference) {
    $hint = $item.HintPath -replace '\$\(GameDir\)', $GameDir
    if (![IO.Path]::IsPathRooted($hint)) { $hint = Join-Path $root $hint }
    [void]$paths.Add([IO.Path]::GetFullPath($hint))
}
[void]$paths.Add((Join-Path $root 'artifacts/LOMSaveProbe/Newtonsoft.Json.dll'))
foreach ($name in @('mscorlib', 'System', 'System.Core', 'netstandard')) {
    [void]$paths.Add((Join-Path $GameDir "Mortal_Data/Managed/$name.dll"))
}
$references = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($path in $paths) { $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($path)) }
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary).
    WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithDeterministic($true)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('LOMSaveProbe', $trees, $references, $options)
$directory = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $root $OutputDirectory }
[void][IO.Directory]::CreateDirectory($directory)
$output = Join-Path $directory 'LOMSaveProbe.dll'
$temporary = $output + '.tmp'
$stream = [IO.File]::Create($temporary)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
$diagnostics = @($result.Diagnostics | Where-Object Severity -ne Hidden | ForEach-Object { $_.ToString() })
foreach ($group in ($diagnostics | Group-Object)) {
    if ($group.Count -gt 1) { "$($group.Name) [$($group.Count) occurrences]" } else { $group.Name }
}
if (!$result.Success) { throw 'Plugin compilation failed; previous DLL was retained.' }
Move-Item -LiteralPath $temporary -Destination $output -Force
Get-FileHash -LiteralPath $output -Algorithm SHA256 | Select-Object Path, Hash
