param(
    [string]$GameDir = 'C:\Game\Steam\steamapps\common\LegendOfMortal',
    [string]$OutputPath = 'artifacts\LOMSaveProbe\LOMSaveProbe.dll'
)
$ErrorActionPreference = 'Stop'
# PowerShell 7 ships Roslyn. Compile against the game's Mono reference assemblies,
# not the host's .NET runtime, so no machine-wide SDK installation is needed.
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
[xml]$project = Get-Content (Join-Path $PSScriptRoot 'LOMSaveProbe.csproj')
$parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion(
    [Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp7_3)
$trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($item in $project.Project.ItemGroup.Compile) {
    $file = Join-Path $PSScriptRoot $item.Include
    $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        [IO.File]::ReadAllText($file), $parseOptions, $file))
}
$referencePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($item in $project.Project.ItemGroup.Reference) {
    $path = $item.HintPath.Replace('$(GameDir)', $GameDir)
    [void]$referencePaths.Add([IO.Path]::GetFullPath($path))
}
foreach ($name in 'mscorlib.dll','netstandard.dll','System.dll','System.Core.dll') {
    [void]$referencePaths.Add((Join-Path $GameDir "Mortal_Data\Managed\$name"))
}
[void]$referencePaths.Add((Join-Path $PSScriptRoot 'artifacts\LOMSaveProbe\Newtonsoft.Json.dll'))
$references = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($path in $referencePaths) {
    $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($path))
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new(
    [Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary).WithOptimizationLevel(
    [Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithDeterministic($true)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create(
    'LOMSaveProbe', $trees, $references, $options)
$output = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $PSScriptRoot $OutputPath }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output))
$stream = [IO.MemoryStream]::new()
$result = $compilation.Emit($stream)
foreach ($message in ($result.Diagnostics |
    Where-Object { $_.Severity -ne [Microsoft.CodeAnalysis.DiagnosticSeverity]::Hidden } |
    ForEach-Object { $_.ToString() } | Sort-Object -Unique)) { Write-Output $message }
if (!$result.Success) { $stream.Dispose(); throw 'Compilation failed.' }
[IO.File]::WriteAllBytes($output, $stream.ToArray())
$stream.Dispose()
Write-Output "Built $output"
Get-FileHash -LiteralPath $output -Algorithm SHA256
