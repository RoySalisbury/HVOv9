# PowerShell Test Script for HVOv9
# This script helps run tests and check the MSTest standardization status

param(
    [string]$Project = "",
    [switch]$All,
    [switch]$BuildOnly,
    [switch]$Verbose
)

$ErrorActionPreference = "Continue"

Write-Host "HVOv9 MSTest Testing Script" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Green

# Set verbosity
$VerbosityFlag = if ($Verbose) { "--verbosity normal" } else { "--verbosity quiet" }

# Function to run tests for a specific project
function Test-Project {
    param([string]$ProjectName)
    
    Write-Host "`nTesting: $ProjectName" -ForegroundColor Cyan
    Write-Host "----------------------------------------" -ForegroundColor Gray
    
    if ($BuildOnly) {
        & dotnet build $ProjectName $VerbosityFlag.Split()
    } else {
        & dotnet test $ProjectName $VerbosityFlag.Split()
    }
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ $ProjectName - SUCCESS" -ForegroundColor Green
    } else {
        Write-Host "❌ $ProjectName - FAILED" -ForegroundColor Red
    }
    
    return $LASTEXITCODE -eq 0
}

# Change to source directory
Set-Location "src"

# Define test projects and their status
$TestProjects = @{
    "HVO.Iot.Devices.Tests" = @{
        Path = "HVO.Iot.Devices.Tests"
        Status = "✅ MSTest Ready"
        Description = "IoT device tests with dependency injection"
    }
    "HVO.WebSite.Playground.Tests" = @{
        Path = "HVO.WebSite.Playground.Tests"
        Status = "✅ MSTest Converted"
        Description = "Web application tests converted to MSTest"
    }
    "HVO.WebSite.RoofControllerV4.Tests" = @{
        Path = "HVO.WebSite.RoofControllerV4.Tests"
        Status = "🔄 Conversion in Progress"
        Description = "Requires manual syntax fixes after conversion"
    }
}

# Show project status
Write-Host "`nTest Project Status:" -ForegroundColor Yellow
Write-Host "===================" -ForegroundColor Yellow
foreach ($proj in $TestProjects.GetEnumerator()) {
    Write-Host "$($proj.Value.Status) $($proj.Key)" -ForegroundColor White
    Write-Host "    $($proj.Value.Description)" -ForegroundColor Gray
}

# Run tests based on parameters
if ($Project -ne "") {
    # Test specific project
    if ($TestProjects.ContainsKey($Project)) {
        $result = Test-Project $TestProjects[$Project].Path
        exit $(if ($result) { 0 } else { 1 })
    } else {
        Write-Host "❌ Project '$Project' not found. Available projects:" -ForegroundColor Red
        $TestProjects.Keys | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }
        exit 1
    }
} elseif ($All) {
    # Test all projects
    $results = @()
    foreach ($proj in $TestProjects.GetEnumerator()) {
        $results += Test-Project $proj.Value.Path
    }
    
    Write-Host "`nSummary:" -ForegroundColor Yellow
    Write-Host "========" -ForegroundColor Yellow
    $successCount = ($results | Where-Object { $_ -eq $true }).Count
    $totalCount = $results.Count
    Write-Host "$successCount/$totalCount projects passed" -ForegroundColor $(if ($successCount -eq $totalCount) { "Green" } else { "Yellow" })
    
    exit $(if ($successCount -eq $totalCount) { 0 } else { 1 })
} else {
    # Show usage
    Write-Host "`nUsage:" -ForegroundColor Yellow
    Write-Host "  .\test.ps1 -Project 'HVO.Iot.Devices.Tests'  # Test specific project"
    Write-Host "  .\test.ps1 -All                               # Test all projects"
    Write-Host "  .\test.ps1 -All -BuildOnly                    # Build only (no test execution)"
    Write-Host "  .\test.ps1 -All -Verbose                      # Verbose output"
    Write-Host ""
    Write-Host "MSTest Standardization Notes:" -ForegroundColor Cyan
    Write-Host "- See MSTest_Standardization.md for complete details"
    Write-Host "- HVO.Iot.Devices.Tests: ✅ Fully functional with DI"
    Write-Host "- HVO.WebSite.Playground.Tests: ✅ Successfully converted"
    Write-Host "- HVO.WebSite.RoofControllerV4.Tests: 🔄 Needs manual syntax fixes"
}

Write-Host ""
