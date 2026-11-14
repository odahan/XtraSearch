param([string]$SdkVersion = '11.0.100')
$ErrorActionPreference = 'Stop'
$dotnetExecutable = (Get-Command dotnet).Source
$dotnetRoot = Split-Path $dotnetExecutable
$sdk = Join-Path $dotnetRoot "sdk/$SdkVersion"
$referenceRoot = Join-Path $dotnetRoot 'packs/Microsoft.NETCore.App.Ref/11.0.0/ref/net11.0'
$taskFolder = $PSScriptRoot
$referenceArguments = Get-ChildItem $referenceRoot -Filter '*.dll' | ForEach-Object { '/r:"' + $_.FullName + '"' }
$compilerArguments = @(
    '/target:library'
    ('/out:"' + (Join-Path $taskFolder 'XtraSearch.Compiler.Rebuilt.dll') + '"')
    ('/r:OriginalCompiler="' + (Join-Path $sdk 'Roslyn/Microsoft.Build.Tasks.CodeAnalysis.dll') + '"')
)
foreach ($assembly in @('Microsoft.Build.Framework.dll', 'Microsoft.Build.Utilities.Core.dll', 'Microsoft.Build.Tasks.Core.dll')) {
    $compilerArguments += '/r:"' + (Join-Path $sdk $assembly) + '"'
}
$compilerArguments += $referenceArguments
$compilerArguments += '"' + (Join-Path $taskFolder 'Csc.cs') + '"'
$responsePath = Join-Path $taskFolder 'compiler.rsp'
$compilerArguments | Set-Content -LiteralPath $responsePath
& $dotnetExecutable (Join-Path $sdk 'Roslyn/bincore/csc.dll') "@$responsePath"
if ($LASTEXITCODE -ne 0) { throw 'Compiler adapter build failed.' }
Write-Output 'Adapter rebuilt as XtraSearch.Compiler.Rebuilt.dll. After closing MSBuild hosts, replace XtraSearch.Compiler.dll with this file.'
