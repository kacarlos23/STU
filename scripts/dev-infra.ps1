$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repositoryRoot

docker compose up -d database
docker compose ps

Write-Host 'PostGIS is ready. Run the API and web applications from separate terminals.'
