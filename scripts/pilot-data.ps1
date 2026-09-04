param(
    [Parameter(Position = 0)]
    [ValidateSet('configure', 'start', 'seed', 'status', 'clean', 'stop')]
    [string]$Action = 'status'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$pilotCompose = Join-Path $repositoryRoot 'compose.pilot.yaml'
$manifestPath = Join-Path $repositoryRoot 'data\pilot\manifest.json'
$pilotProject = Join-Path $repositoryRoot 'tools\STU.PilotData\STU.PilotData.csproj'
Set-Location -LiteralPath $repositoryRoot

function Import-PilotEnvironment {
    $env:STU_PILOT_POSTGRES_PASSWORD = [Environment]::GetEnvironmentVariable('STU_PILOT_POSTGRES_PASSWORD', 'User')
    $env:STU_PILOT_ACCOUNT_PASSWORD = [Environment]::GetEnvironmentVariable('STU_PILOT_ACCOUNT_PASSWORD', 'User')
    $env:STU_PILOT_DB_CONNECTION = [Environment]::GetEnvironmentVariable('STU_PILOT_DB_CONNECTION', 'User')

    if ([string]::IsNullOrWhiteSpace($env:STU_PILOT_POSTGRES_PASSWORD) -or
        [string]::IsNullOrWhiteSpace($env:STU_PILOT_ACCOUNT_PASSWORD) -or
        [string]::IsNullOrWhiteSpace($env:STU_PILOT_DB_CONNECTION)) {
        throw 'O ambiente piloto ainda não foi configurado. Execute .\scripts\pilot-data.ps1 configure.'
    }
}

function Configure-PilotEnvironment {
    $databasePassword = [Environment]::GetEnvironmentVariable('STU_PILOT_POSTGRES_PASSWORD', 'User')
    if ([string]::IsNullOrWhiteSpace($databasePassword)) {
        $databasePassword = 'StuPilotDb!' + [Guid]::NewGuid().ToString('N')
        [Environment]::SetEnvironmentVariable('STU_PILOT_POSTGRES_PASSWORD', $databasePassword, 'User')
    }

    $accountPassword = [Environment]::GetEnvironmentVariable('STU_PILOT_ACCOUNT_PASSWORD', 'User')
    if ([string]::IsNullOrWhiteSpace($accountPassword)) {
        $accountPassword = 'StuPilotUser!9-' + [Guid]::NewGuid().ToString('N')
        [Environment]::SetEnvironmentVariable('STU_PILOT_ACCOUNT_PASSWORD', $accountPassword, 'User')
    }

    $connection = "Host=127.0.0.1;Port=55433;Database=stu_load_pilot;Username=stu_pilot;Password=$databasePassword"
    [Environment]::SetEnvironmentVariable('STU_PILOT_DB_CONNECTION', $connection, 'User')
    Import-PilotEnvironment
    Write-Host 'Ambiente piloto configurado no perfil do usuário. Nenhuma senha foi gravada no repositório.'
}

function Start-PilotServices {
    docker compose -f $pilotCompose up -d --build pilot-database pilot-operations-init pilot-api pilot-worker
    if ($LASTEXITCODE -ne 0) {
        throw 'Não foi possível iniciar os serviços do piloto.'
    }
}

if ($Action -eq 'configure') {
    Configure-PilotEnvironment
    exit 0
}

Import-PilotEnvironment

switch ($Action) {
    'start' {
        Start-PilotServices
    }
    'seed' {
        docker compose -f $pilotCompose up -d pilot-database
        if ($LASTEXITCODE -ne 0) {
            throw 'Não foi possível iniciar o banco do piloto.'
        }

        $ready = $false
        foreach ($attempt in 1..30) {
            $health = docker inspect stu-pilot-pilot-database-1 --format '{{.State.Health.Status}}' 2>$null
            if ($health -eq 'healthy') {
                $ready = $true
                break
            }

            Start-Sleep -Seconds 2
        }

        if (-not $ready) {
            throw 'O banco do piloto não ficou pronto no tempo esperado.'
        }

        dotnet run --project $pilotProject --configuration Release -- seed --manifest $manifestPath
        if ($LASTEXITCODE -ne 0) {
            throw 'A carga dos dados sintéticos falhou.'
        }

        docker compose -f $pilotCompose up -d --build pilot-operations-init pilot-api pilot-worker
        if ($LASTEXITCODE -ne 0) {
            throw 'Os dados foram carregados, mas a API ou o worker do piloto não iniciou.'
        }
    }
    'status' {
        docker compose -f $pilotCompose ps
        dotnet run --project $pilotProject --configuration Release -- status
    }
    'clean' {
        docker compose -f $pilotCompose stop pilot-api pilot-worker
        dotnet run --project $pilotProject --configuration Release -- clean
        if ($LASTEXITCODE -ne 0) {
            throw 'A limpeza isolada do piloto falhou.'
        }
    }
    'stop' {
        docker compose -f $pilotCompose stop
    }
}
