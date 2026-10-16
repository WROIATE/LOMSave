param(
    [string]$GameDir = 'C:/Game/Steam/steamapps/common/LegendOfMortal',
    [string]$Assembly = 'Mortal.Core',
    [string]$Type = '.',
    [string]$Method = '^$',
    [switch]$IncludeNested
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameDir 'BepInEx/core/Mono.Cecil.dll')
$definition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDir "Mortal_Data/Managed/$Assembly.dll"))
try {
    foreach ($item in $definition.MainModule.GetTypes() | Where-Object { $_.FullName -match $Type -and ($IncludeNested -or !$_.IsNested) }) {
        "TYPE $($item.FullName)"
        foreach ($field in $item.Fields) { "FIELD $field = $($field.Constant)" }
        foreach ($member in $item.Methods) {
            "METHOD $member"
            if ($member.Name -match $Method -and $member.HasBody) {
                foreach ($instruction in $member.Body.Instructions) { $instruction.ToString() }
            }
        }
    }
} finally { $definition.Dispose() }
