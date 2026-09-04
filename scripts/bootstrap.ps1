$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repositoryRoot

if (-not (Test-Path -LiteralPath '.env')) {
    Copy-Item -LiteralPath '.env.example' -Destination '.env'
    Write-Warning 'A development .env file was created. Change its passwords before sharing the environment.'
}

dotnet tool restore
dotnet restore STU.slnx
npm ci

Write-Host 'STU dependencies are ready.'
