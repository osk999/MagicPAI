# scripts/dev-up.ps1
# Brings up the full dev stack on Windows. See temporal.md §UU.1.
param(
    [switch]$Clean,
    [switch]$Rebuild,
    [switch]$SkipWorkerBuild
)

$ErrorActionPreference = 'Stop'

Write-Host "=== MagicPAI Dev Up ===" -ForegroundColor Cyan

$composeBase = "docker/docker-compose.yml"
$composeTemporal = "docker/docker-compose.temporal.yml"
$composeDev = "docker/docker-compose.dev.yml"

if ($Clean) {
    Write-Host "Cleaning volumes..." -ForegroundColor Yellow
    docker compose -f $composeBase -f $composeTemporal down -v
}

if ($Rebuild) {
    Write-Host "Rebuilding server image..." -ForegroundColor Yellow
    docker compose -f $composeBase build server
}

if (-not $SkipWorkerBuild) {
    $workerImage = (docker image inspect magicpai-env:latest 2>$null)
    if (-not $workerImage) {
        Write-Host "Building worker-env image..." -ForegroundColor Yellow
        docker compose -f $composeBase --profile build build worker-env-builder
    } else {
        Write-Host "worker-env:latest already built; skipping (use -Rebuild to force)." -ForegroundColor DarkGray
    }
}

Write-Host "Starting stack..." -ForegroundColor Green
docker compose -f $composeBase -f $composeTemporal -f $composeDev up -d

Write-Host "Waiting for services..."
$timeout = 60
$elapsed = 0
$ready = $false
while ($elapsed -lt $timeout) {
    try {
        $r = Invoke-WebRequest -Uri http://localhost:5000/health -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
        if ($r.StatusCode -eq 200) {
            $ready = $true
            break
        }
    } catch { }
    Start-Sleep -Seconds 2
    $elapsed += 2
}

if ($ready) {
    Write-Host "[+] Server ready" -ForegroundColor Green
} else {
    Write-Host "[-] Server not ready after $timeout sec" -ForegroundColor Yellow
    Write-Host "Check: docker compose logs server" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Endpoints:" -ForegroundColor Cyan
Write-Host "  MagicPAI Studio: http://localhost:5000"
Write-Host "  Temporal UI:     http://localhost:8233"
Write-Host "  Swagger:         http://localhost:5000/swagger"
Write-Host "  Prometheus:      http://localhost:5000/metrics"
