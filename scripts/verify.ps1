$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repositoryRoot

dotnet build STU.slnx --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'A compilação .NET falhou.' }
dotnet test STU.slnx --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Os testes .NET falharam.' }
npm run lint:web
if ($LASTEXITCODE -ne 0) { throw 'A análise estática das interfaces falhou.' }
npm run test:web
if ($LASTEXITCODE -ne 0) { throw 'Os testes das interfaces falharam.' }
npm run build:web
if ($LASTEXITCODE -ne 0) { throw 'A compilação das interfaces falhou.' }
docker compose --env-file .env.example config --quiet
if ($LASTEXITCODE -ne 0) { throw 'A configuração Docker principal é inválida.' }

if ([string]::IsNullOrWhiteSpace($env:STU_PILOT_POSTGRES_PASSWORD)) {
    $env:STU_PILOT_POSTGRES_PASSWORD = 'verification-only'
}
docker compose -f compose.pilot.yaml config --quiet
if ($LASTEXITCODE -ne 0) { throw 'A configuração Docker do piloto é inválida.' }

Write-Host 'STU verification completed successfully.'
