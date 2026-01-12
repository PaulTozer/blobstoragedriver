<#
.SYNOPSIS
    Build and package Blob Storage Driver
    
.DESCRIPTION
    Publishes all projects and creates an installer package
    
.PARAMETER Configuration
    Build configuration (Debug/Release). Default: Release
    
.PARAMETER SkipBuild
    Skip the build step (use existing build output)
    
.EXAMPLE
    .\Build.ps1
    
.EXAMPLE
    .\Build.ps1 -Configuration Debug
#>

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionDir = Split-Path -Parent $ScriptDir

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Blob Storage Driver Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration: $Configuration" -ForegroundColor Gray
Write-Host "Solution Dir:  $SolutionDir" -ForegroundColor Gray
Write-Host ""

# Output directory
$OutputDir = Join-Path $SolutionDir "publish"
$InstallerOutput = Join-Path $OutputDir "Installer"

if (!$SkipBuild) {
    # Clean output directory
    if (Test-Path $OutputDir) {
        Write-Host "Cleaning output directory..." -ForegroundColor Yellow
        Remove-Item -Path $OutputDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    New-Item -ItemType Directory -Path $InstallerOutput -Force | Out-Null

    # Restore packages
    Write-Host "Restoring packages..." -ForegroundColor Green
    dotnet restore "$SolutionDir\BlobStorageDriver.sln"

    # Build solution
    Write-Host "Building solution..." -ForegroundColor Green
    dotnet build "$SolutionDir\BlobStorageDriver.sln" -c $Configuration --no-restore

    # Publish WinUI app
    Write-Host "Publishing WinUI application..." -ForegroundColor Green
    $WinUIProject = Join-Path $SolutionDir "src\BlobStorageDriver.WinUI\BlobStorageDriver.WinUI.csproj"
    $WinUIOutput = Join-Path $OutputDir "TrayApp"
    dotnet publish $WinUIProject -c $Configuration -r win-x64 --self-contained true -o $WinUIOutput

    # Publish Service
    Write-Host "Publishing Windows Service..." -ForegroundColor Green
    $ServiceProject = Join-Path $SolutionDir "src\BlobStorageDriver.Service\BlobStorageDriver.Service.csproj"
    $ServiceOutput = Join-Path $OutputDir "Service"
    dotnet publish $ServiceProject -c $Configuration -r win-x64 --self-contained true -o $ServiceOutput
}

# Copy installer files
Write-Host "Preparing installer..." -ForegroundColor Green
$InstallerSrc = Join-Path $SolutionDir "src\BlobStorageDriver.Installer"
Copy-Item -Path (Join-Path $InstallerSrc "Install.ps1") -Destination $InstallerOutput
Copy-Item -Path (Join-Path $InstallerSrc "License.rtf") -Destination $InstallerOutput

# Copy application files to installer output
if (Test-Path (Join-Path $OutputDir "TrayApp")) {
    $TrayAppDest = Join-Path $InstallerOutput "TrayApp"
    New-Item -ItemType Directory -Path $TrayAppDest -Force | Out-Null
    Copy-Item -Path (Join-Path $OutputDir "TrayApp\*") -Destination $TrayAppDest -Recurse -Force
}

if (Test-Path (Join-Path $OutputDir "Service")) {
    $ServiceDest = Join-Path $InstallerOutput "Service"
    New-Item -ItemType Directory -Path $ServiceDest -Force | Out-Null
    Copy-Item -Path (Join-Path $OutputDir "Service\*") -Destination $ServiceDest -Recurse -Force
}

# Create self-extracting installer script
$SelfExtractScript = @'
<#
.SYNOPSIS
    Blob Storage Driver Installer
    
.DESCRIPTION
    Self-contained installer for Blob Storage Driver
    
.PARAMETER InstallService
    Install and start the Windows service
    
.PARAMETER AutoStartTray  
    Configure tray app to start automatically at Windows login
    
.PARAMETER Silent
    Run in silent mode (no prompts)
#>

[CmdletBinding()]
param(
    [switch]$InstallService,
    [switch]$AutoStartTray,
    [switch]$Silent
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'BlobStorageDriver'
$AppName = 'Blob Storage Driver'
$TrayAppExe = 'BlobStorageDriver.WinUI.exe'
$ServiceExe = 'BlobStorageDriver.Service.exe'
$InstallPath = "$env:ProgramFiles\BlobStorageDriver"
$RegistryRunKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'

function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (!(Test-Administrator)) {
    Write-Host "This installer requires administrator privileges." -ForegroundColor Red
    Write-Host "Please run PowerShell as Administrator and try again." -ForegroundColor Yellow
    if (!$Silent) { Read-Host "Press Enter to exit" }
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Blob Storage Driver Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Determine script location (where files are extracted)
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Create installation directory
Write-Host "Installing to: $InstallPath" -ForegroundColor Green
if (!(Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
}

$TrayAppPath = Join-Path $InstallPath "TrayApp"
$ServicePath = Join-Path $InstallPath "Service"

# Copy files
Write-Host "Copying application files..." -ForegroundColor Gray

$SourceTrayApp = Join-Path $ScriptDir "TrayApp"
$SourceService = Join-Path $ScriptDir "Service"

if (Test-Path $SourceTrayApp) {
    if (!(Test-Path $TrayAppPath)) { New-Item -ItemType Directory -Path $TrayAppPath -Force | Out-Null }
    Copy-Item -Path "$SourceTrayApp\*" -Destination $TrayAppPath -Recurse -Force
    Write-Host "  Tray Application installed" -ForegroundColor Gray
}

if (Test-Path $SourceService) {
    if (!(Test-Path $ServicePath)) { New-Item -ItemType Directory -Path $ServicePath -Force | Out-Null }
    Copy-Item -Path "$SourceService\*" -Destination $ServicePath -Recurse -Force
    Write-Host "  Windows Service installed" -ForegroundColor Gray
}

# Create Start Menu shortcuts
Write-Host "Creating shortcuts..." -ForegroundColor Gray
$StartMenuPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\$AppName"
New-Item -ItemType Directory -Path $StartMenuPath -Force | Out-Null

$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut("$StartMenuPath\Blob Storage Driver.lnk")
$Shortcut.TargetPath = Join-Path $TrayAppPath $TrayAppExe
$Shortcut.WorkingDirectory = $TrayAppPath
$Shortcut.Description = "Azure Blob Storage file system driver"
$Shortcut.Save()

# Install Windows Service if requested
if ($InstallService) {
    Write-Host "Installing Windows Service..." -ForegroundColor Green
    $ServiceExePath = Join-Path $ServicePath $ServiceExe
    
    # Stop and remove existing service
    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existingService) {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }
    
    # Install new service
    New-Service -Name $ServiceName `
                -BinaryPathName $ServiceExePath `
                -DisplayName "Blob Storage Driver Service" `
                -Description "Background synchronization service for Azure Blob Storage" `
                -StartupType Automatic | Out-Null
    
    Start-Service -Name $ServiceName
    Write-Host "  Service installed and started" -ForegroundColor Gray
}

# Configure auto-start if requested
if ($AutoStartTray) {
    Write-Host "Configuring auto-start..." -ForegroundColor Green
    $TrayExePath = Join-Path $TrayAppPath $TrayAppExe
    Set-ItemProperty -Path $RegistryRunKey -Name $ServiceName -Value "`"$TrayExePath`" --minimized"
    Write-Host "  Auto-start enabled" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host ""

if (!$Silent) {
    $launch = Read-Host "Launch Blob Storage Driver now? (Y/n)"
    if ($launch -ne 'n' -and $launch -ne 'N') {
        Start-Process -FilePath (Join-Path $TrayAppPath $TrayAppExe) -ArgumentList "--minimized"
        Write-Host "Application launched (minimized to system tray)" -ForegroundColor Cyan
    }
}
'@

$SelfExtractPath = Join-Path $InstallerOutput "Setup.ps1"
Set-Content -Path $SelfExtractPath -Value $SelfExtractScript

# Create a simple batch launcher
$BatchLauncher = @"
@echo off
echo Blob Storage Driver Installer
echo.
echo This installer requires Administrator privileges.
echo.
PowerShell -ExecutionPolicy Bypass -File "%~dp0Setup.ps1" -InstallService -AutoStartTray
pause
"@

$BatchPath = Join-Path $InstallerOutput "Install.bat"
Set-Content -Path $BatchPath -Value $BatchLauncher

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Output directory: $OutputDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "Installer package: $InstallerOutput" -ForegroundColor Cyan
Write-Host "  - Setup.ps1     : PowerShell installer script" -ForegroundColor Gray
Write-Host "  - Install.bat   : Double-click installer (with service + auto-start)" -ForegroundColor Gray
Write-Host "  - TrayApp\      : System tray application" -ForegroundColor Gray
Write-Host "  - Service\      : Windows service" -ForegroundColor Gray
Write-Host ""
Write-Host "To install:" -ForegroundColor Yellow
Write-Host "  1. Right-click Install.bat and 'Run as administrator'" -ForegroundColor Gray
Write-Host "  Or run PowerShell as admin:" -ForegroundColor Gray
Write-Host "  .\Setup.ps1 -InstallService -AutoStartTray" -ForegroundColor Gray
Write-Host ""
