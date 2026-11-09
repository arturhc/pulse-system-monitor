#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts/win-x64'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    & dotnet restore Pulse.csproj -r win-x64 --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }
    & dotnet publish Pulse.csproj -c $Configuration -r win-x64 --self-contained true --no-restore -o $OutputDirectory -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
    Copy-Item -LiteralPath (Join-Path $output 'Pulse.exe') -Destination (Join-Path $output 'Instalar Pulse.exe') -Force
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $output -Force
    Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $output -Recurse -Force
    Write-Host "Built Pulse: $output"
}
finally { Pop-Location }
