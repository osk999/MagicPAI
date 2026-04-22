# scripts/backup.ps1
# Back up both PostgreSQL databases to local disk.
# See temporal.md §18.5 and §UU.5.
param(
    [string]$BackupRoot = "C:\mpai-backups",
    [int]$RetentionDays = 14
)

$ErrorActionPreference = 'Stop'

$date = Get-Date -Format 'yyyy-MM-dd'
$dir = Join-Path $BackupRoot $date
New-Item -ItemType Directory -Force -Path $dir | Out-Null

Write-Host "=== MagicPAI backup ===" -ForegroundColor Cyan
Write-Host "Target: $dir"

# Temporal DB
Write-Host "Dumping temporal database..." -ForegroundColor Yellow
docker exec mpai-temporal-db pg_dump -U temporal temporal `
    | Out-File -Encoding utf8 (Join-Path $dir "temporal-$date.sql")
$tempSize = (Get-Item (Join-Path $dir "temporal-$date.sql")).Length / 1MB
Write-Host "  temporal-$date.sql ($([math]::Round($tempSize, 2)) MB)"

# MagicPAI DB
Write-Host "Dumping magicpai database..." -ForegroundColor Yellow
docker exec mpai-db pg_dump -U magicpai magicpai `
    | Out-File -Encoding utf8 (Join-Path $dir "magicpai-$date.sql")
$mpaiSize = (Get-Item (Join-Path $dir "magicpai-$date.sql")).Length / 1MB
Write-Host "  magicpai-$date.sql ($([math]::Round($mpaiSize, 2)) MB)"

# Compress
Write-Host "Compressing..." -ForegroundColor Yellow
Compress-Archive -Path "$dir\*" -DestinationPath "$dir.zip" -Force
$zipSize = (Get-Item "$dir.zip").Length / 1MB
Write-Host "Archive: $dir.zip ($([math]::Round($zipSize, 2)) MB)" -ForegroundColor Green
Remove-Item -Recurse -Force $dir

# Prune old backups
$cutoff = (Get-Date).AddDays(-$RetentionDays)
$pruned = 0
Get-ChildItem -Path $BackupRoot -Filter "*.zip" |
    Where-Object { $_.LastWriteTime -lt $cutoff } |
    ForEach-Object {
        Remove-Item -Force $_.FullName
        $pruned++
    }
if ($pruned -gt 0) {
    Write-Host "Pruned $pruned backups older than $RetentionDays days." -ForegroundColor DarkGray
}

Write-Host "Backup complete." -ForegroundColor Green
