param(
    [Parameter(Position = 0)]
    [ValidateSet('smoke', 'baseline', 'gate')]
    [string]$Profile = 'smoke'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$pilotCompose = Join-Path $repositoryRoot 'compose.pilot.yaml'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$resultName = "$timestamp-$Profile"
$resultDirectory = Join-Path $repositoryRoot "data\pilot\load\$resultName"
$standardOutput = Join-Path $resultDirectory 'k6-output.txt'
$standardError = Join-Path $resultDirectory 'k6-error.txt'
$containerMetrics = Join-Path $resultDirectory 'container-resources.csv'
$hostMetrics = Join-Path $resultDirectory 'host-resources.csv'
$databaseMetrics = Join-Path $resultDirectory 'database-connections.csv'

New-Item -ItemType Directory -Force -Path $resultDirectory | Out-Null
Set-Location -LiteralPath $repositoryRoot

$env:STU_PILOT_POSTGRES_PASSWORD = [Environment]::GetEnvironmentVariable('STU_PILOT_POSTGRES_PASSWORD', 'User')
$env:STU_PILOT_ACCOUNT_PASSWORD = [Environment]::GetEnvironmentVariable('STU_PILOT_ACCOUNT_PASSWORD', 'User')
$env:STU_PILOT_DB_CONNECTION = [Environment]::GetEnvironmentVariable('STU_PILOT_DB_CONNECTION', 'User')
$env:STU_PASSWORD = $env:STU_PILOT_ACCOUNT_PASSWORD

if ([string]::IsNullOrWhiteSpace($env:STU_PILOT_POSTGRES_PASSWORD) -or
    [string]::IsNullOrWhiteSpace($env:STU_PASSWORD)) {
    throw 'Configure o ambiente piloto antes do teste de carga.'
}

$readiness = Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:8092/health/ready' -TimeoutSec 20
if ($readiness.StatusCode -ne 200) {
    throw 'A API piloto não está pronta para o teste.'
}

$containerNames = @(
    'stu-pilot-pilot-api-1',
    'stu-pilot-pilot-database-1',
    'stu-pilot-pilot-worker-1'
)

'timestamp,name,cpu,mem_usage,net_io,block_io,pids' | Set-Content -LiteralPath $containerMetrics
'timestamp,free_physical_memory_kb,cpu_load_percent' | Set-Content -LiteralPath $hostMetrics
'timestamp,active,total' | Set-Content -LiteralPath $databaseMetrics

$dockerArguments = @(
    'run', '--rm',
    '--name', "stu-pilot-k6-$timestamp",
    '--add-host', 'host.docker.internal:host-gateway',
    '-e', 'STU_PASSWORD',
    '-e', "STU_PROFILE=$Profile",
    '-e', 'STU_BASE_URL=http://host.docker.internal:8092',
    '-e', "STU_RESULT_PATH=/work/data/pilot/load/$resultName/summary.json",
    '-v', "${repositoryRoot}:/work",
    'grafana/k6:1.5.0',
    'run', '--include-system-env-vars', '/work/tests/load/stu-pilot.js'
)

$process = Start-Process -FilePath 'docker' -ArgumentList $dockerArguments -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $standardOutput -RedirectStandardError $standardError

while (-not $process.HasExited) {
    $sampleTime = [DateTimeOffset]::UtcNow.ToString('O')
    $dockerRows = docker stats --no-stream --format '{{.Name}},{{.CPUPerc}},{{.MemUsage}},{{.NetIO}},{{.BlockIO}},{{.PIDs}}' $containerNames 2>$null
    foreach ($row in $dockerRows) {
        Add-Content -LiteralPath $containerMetrics -Value "$sampleTime,$row"
    }

    $operatingSystem = Get-CimInstance Win32_OperatingSystem
    $processor = Get-CimInstance Win32_Processor | Select-Object -First 1
    Add-Content -LiteralPath $hostMetrics -Value "$sampleTime,$($operatingSystem.FreePhysicalMemory),$($processor.LoadPercentage)"

    try {
        $connectionCounts = docker exec stu-pilot-pilot-database-1 psql -U stu_pilot -d stu_load_pilot -At -F ',' -c `
            "SELECT count(*) FILTER (WHERE state = 'active'), count(*) FROM pg_stat_activity WHERE datname = current_database();" 2>$null
    }
    catch {
        $connectionCounts = 'unavailable,unavailable'
    }
    Add-Content -LiteralPath $databaseMetrics -Value "$sampleTime,$connectionCounts"
    Start-Sleep -Seconds 5
    $process.Refresh()
}

$process.WaitForExit()
Get-Content -LiteralPath $standardOutput
if ((Get-Item -LiteralPath $standardError).Length -gt 0) {
    Get-Content -LiteralPath $standardError
}

$restartState = docker inspect $containerNames --format '{{.Name}},{{.State.Status}},{{.RestartCount}}'
$restartState | Set-Content -LiteralPath (Join-Path $resultDirectory 'container-state.txt')

$serviceIssues = docker compose -f $pilotCompose logs --no-color --since 15m pilot-api pilot-worker |
    Select-String -Pattern 'fail|error|exception|critical|fatal' -CaseSensitive:$false
if ($serviceIssues) {
    $serviceIssues | Set-Content -LiteralPath (Join-Path $resultDirectory 'service-issues.txt')
}

Write-Host "Resultados: $resultDirectory"
exit $process.ExitCode
