param(
    [int]$Runs = 2,
    [string]$Config = "appsettings.local.json"
)

$ErrorActionPreference = "Stop"
$projectDir = Split-Path -Parent $PSScriptRoot
Push-Location $projectDir

try {
    $failures = 0
    for ($i = 1; $i -le $Runs; $i++) {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $logPath = Join-Path $projectDir "live-run-back-to-back-$i-$timestamp.log"
        Write-Host "=== Live run $i of $Runs ==="
        dotnet run -- --live --config $Config 2>&1 | Tee-Object -FilePath $logPath
        if ($LASTEXITCODE -ne 0) {
            $failures++
            Write-Host "Run $i FAILED (exit $LASTEXITCODE). Log: $logPath"
        }
        else {
            Write-Host "Run $i SUCCESS. Log: $logPath"
        }
    }

    if ($failures -gt 0) {
        Write-Error "$failures of $Runs live runs failed."
        exit 1
    }

    Write-Host "All $Runs live runs succeeded."
    exit 0
}
finally {
    Pop-Location
}
