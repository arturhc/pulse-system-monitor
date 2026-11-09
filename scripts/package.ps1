#requires -Version 7.2
[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (-not $SkipBuild) { & "$PSScriptRoot/build.ps1" }
    $project = [xml](Get-Content -LiteralPath Pulse.csproj -Raw)
    $version = $project.Project.PropertyGroup.Version | Select-Object -First 1
    $directory = Join-Path $root 'artifacts/packages'
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $name = "Pulse-System-Monitor-v$version-win-x64.zip"
    $zip = Join-Path $directory $name
    Compress-Archive -Path (Join-Path $root 'artifacts/win-x64/*') -DestinationPath $zip -CompressionLevel Optimal -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $directory "$name.sha256"), "$hash  $name`n", [Text.UTF8Encoding]::new($false))
    Write-Host "Package: $zip"
    Write-Host "SHA-256: $hash"
}
finally { Pop-Location }
