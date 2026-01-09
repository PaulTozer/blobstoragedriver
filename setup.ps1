# Blob Storage Driver - Build and Setup Script
# Run this script as Administrator for service installation

param(
    [switch]$Build,
    [switch]$Run,
    [switch]$InstallService,
    [switch]$UninstallService,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$SolutionDir = $PSScriptRoot
$OutputDir = Join-Path $SolutionDir "out"

function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host ""
}

function Build-Solution {
    Write-Header "Building Blob Storage Driver"
    
    # Restore packages
    Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
    dotnet restore "$SolutionDir\BlobStorageDriver.sln"
    
    # Build solution
    Write-Host "Building solution..." -ForegroundColor Yellow
    dotnet build "$SolutionDir\BlobStorageDriver.sln" -c Release -o $OutputDir
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Build completed successfully!" -ForegroundColor Green
        Write-Host "Output: $OutputDir"
    }
    else {
        Write-Host "Build failed!" -ForegroundColor Red
        exit 1
    }
}

function Run-TrayApp {
    Write-Header "Running Tray Application"
    
    $TrayAppPath = Join-Path $OutputDir "BlobStorageDriver.TrayApp.exe"
    
    if (Test-Path $TrayAppPath) {
        Write-Host "Starting Blob Storage Driver..." -ForegroundColor Yellow
        Start-Process $TrayAppPath
    }
    else {
        Write-Host "Tray application not found. Run with -Build first." -ForegroundColor Red
        exit 1
    }
}

function Install-Service {
    Write-Header "Installing Windows Service"
    
    # Check for admin rights
    if (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
        Write-Host "This operation requires Administrator privileges!" -ForegroundColor Red
        exit 1
    }
    
    $ServiceName = "BlobStorageDriver"
    $ServicePath = Join-Path $OutputDir "BlobStorageDriver.Service.exe"
    
    if (-not (Test-Path $ServicePath)) {
        Write-Host "Service executable not found. Run with -Build first." -ForegroundColor Red
        exit 1
    }
    
    # Check if service exists
    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    
    if ($existingService) {
        Write-Host "Service already exists. Stopping and removing..." -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $ServiceName
        Start-Sleep -Seconds 2
    }
    
    # Create service
    Write-Host "Creating service..." -ForegroundColor Yellow
    $result = sc.exe create $ServiceName binPath= "`"$ServicePath`"" start= auto DisplayName= "Blob Storage Driver"
    
    if ($LASTEXITCODE -eq 0) {
        # Set description
        sc.exe description $ServiceName "Synchronizes files between local storage and Azure Blob Storage"
        
        # Start service
        Write-Host "Starting service..." -ForegroundColor Yellow
        Start-Service -Name $ServiceName
        
        Write-Host "Service installed and started successfully!" -ForegroundColor Green
    }
    else {
        Write-Host "Failed to create service!" -ForegroundColor Red
        exit 1
    }
}

function Uninstall-Service {
    Write-Header "Uninstalling Windows Service"
    
    # Check for admin rights
    if (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
        Write-Host "This operation requires Administrator privileges!" -ForegroundColor Red
        exit 1
    }
    
    $ServiceName = "BlobStorageDriver"
    
    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    
    if ($existingService) {
        Write-Host "Stopping service..." -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        
        Write-Host "Removing service..." -ForegroundColor Yellow
        sc.exe delete $ServiceName
        
        Write-Host "Service uninstalled successfully!" -ForegroundColor Green
    }
    else {
        Write-Host "Service not found." -ForegroundColor Yellow
    }
}

function Clean-Solution {
    Write-Header "Cleaning Build Output"
    
    if (Test-Path $OutputDir) {
        Write-Host "Removing output directory..." -ForegroundColor Yellow
        Remove-Item -Path $OutputDir -Recurse -Force
    }
    
    # Clean bin and obj folders
    Get-ChildItem -Path $SolutionDir -Include bin,obj -Recurse -Directory | ForEach-Object {
        Write-Host "Removing $($_.FullName)" -ForegroundColor Gray
        Remove-Item -Path $_.FullName -Recurse -Force
    }
    
    Write-Host "Clean completed!" -ForegroundColor Green
}

# Main
Write-Host ""
Write-Host "  Blob Storage Driver Setup" -ForegroundColor White
Write-Host "  =========================" -ForegroundColor White

if ($Clean) {
    Clean-Solution
}

if ($Build) {
    Build-Solution
}

if ($InstallService) {
    Install-Service
}

if ($UninstallService) {
    Uninstall-Service
}

if ($Run) {
    Run-TrayApp
}

if (-not $Clean -and -not $Build -and -not $InstallService -and -not $UninstallService -and -not $Run) {
    Write-Host ""
    Write-Host "Usage:" -ForegroundColor Yellow
    Write-Host "  .\setup.ps1 -Build           Build the solution"
    Write-Host "  .\setup.ps1 -Run             Run the tray application"
    Write-Host "  .\setup.ps1 -InstallService  Install as Windows service (requires Admin)"
    Write-Host "  .\setup.ps1 -UninstallService Remove Windows service (requires Admin)"
    Write-Host "  .\setup.ps1 -Clean           Clean build output"
    Write-Host "  .\setup.ps1 -Build -Run      Build and run"
    Write-Host ""
}

Write-Host ""
