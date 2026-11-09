#requires -Version 7.2
[CmdletBinding()]
param([switch]$SkipBuild, [string]$PublishDirectory = 'artifacts/win-x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (-not $SkipBuild) { & "$PSScriptRoot/build.ps1" -OutputDirectory $PublishDirectory }
    & node --test tests/display.test.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Presentation tests failed.' }
    $app = [IO.Path]::GetFullPath((Join-Path $root "$PublishDirectory/Pulse.exe"))
    if (-not (Test-Path -LiteralPath $app)) { throw 'Build the app first.' }
    $testPath = Join-Path $root ('artifacts/tests/' + [guid]::NewGuid().ToString('N'))
    $process = Start-Process -FilePath $app -ArgumentList @('--test-storage', ('"' + $testPath + '"')) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) { throw 'Storage tests exceeded 30 seconds.' }
    if ($process.ExitCode -ne 0) { throw "Storage tests failed. See $testPath/test-result.txt" }
    $result = Get-Content -LiteralPath (Join-Path $testPath 'test-result.txt') -Raw
    if (-not $result.StartsWith('PASS:')) { throw $result }
    Write-Host $result
}
finally { Pop-Location }
