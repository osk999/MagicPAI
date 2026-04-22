# scripts/run-tests.ps1
# Run test categories. See temporal.md §UU.6.
param(
    [ValidateSet("Unit", "Integration", "Replay", "E2E", "All")]
    [string]$Category = "Unit",
    [switch]$Coverage
)

$ErrorActionPreference = 'Stop'

$args = @()
if ($Category -ne "All") {
    $args += "--filter"
    $args += "Category=$Category"
}
if ($Coverage) {
    $args += "--collect:"
    $args += "XPlat Code Coverage"
    $args += "--results-directory"
    $args += "./test-results"
}

Write-Host "=== Running $Category tests ===" -ForegroundColor Cyan
dotnet test @args
