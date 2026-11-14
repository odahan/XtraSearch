#requires -Version 5.1

$ErrorActionPreference = 'Stop'
$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    throw 'Install the .NET 11 SDK and make dotnet available on PATH before publishing.'
}

$project = Join-Path $PSScriptRoot 'app\XtraSearch.csproj'
$publishDirectory = Join-Path $PSScriptRoot 'Publish'
$executable = Join-Path $publishDirectory 'XtraSearch.exe'

Push-Location -LiteralPath $PSScriptRoot
try {
    & $dotnet publish $project -m:1 --configuration Release --runtime win-x64 `
        --self-contained true --output $publishDirectory `
        -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true `
        -p:DebugType=embedded -p:UseLocalCompilerAdapter=false
    if ($LASTEXITCODE -ne 0) {
        throw "XtraSearch publish failed (dotnet exit code $LASTEXITCODE)."
    }
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "The published executable is missing: $executable"
    }

    Write-Host "Published XtraSearch for Windows x64: $executable"
} finally {
    Pop-Location
}
