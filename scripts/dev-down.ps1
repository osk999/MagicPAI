# scripts/dev-down.ps1
# Stop dev stack. Pass -Volumes to also remove persistent data.
param([switch]$Volumes)

$ErrorActionPreference = 'Stop'

$composeBase = "docker/docker-compose.yml"
$composeTemporal = "docker/docker-compose.temporal.yml"

if ($Volumes) {
    Write-Host "Stopping stack and removing volumes..." -ForegroundColor Yellow
    docker compose -f $composeBase -f $composeTemporal down -v
} else {
    Write-Host "Stopping stack (preserving volumes)..." -ForegroundColor Yellow
    docker compose -f $composeBase -f $composeTemporal down
}

Write-Host "Stack stopped." -ForegroundColor Green
