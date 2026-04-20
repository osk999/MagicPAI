# scripts/clean.ps1
# Remove bin/obj directories across the solution. See temporal.md §UU.8.

$ErrorActionPreference = 'Stop'

Write-Host "=== Cleaning build artifacts ===" -ForegroundColor Cyan
Get-ChildItem -Include bin,obj -Recurse -Directory |
    ForEach-Object {
        Write-Host "  Removing $($_.FullName)" -ForegroundColor DarkGray
        Remove-Item -Recurse -Force $_.FullName
    }
Write-Host "Done." -ForegroundColor Green
