<#
.SYNOPSIS
    Blob Storage Driver Installer Script
    
.DESCRIPTION
    Installs or uninstalls Blob Storage Driver with options for:
    - Windows Service installation
    - Auto-start tray application at Windows startup
    
.PARAMETER Install
    Install the application (default action)
    
.PARAMETER Uninstall
    Uninstall the application
    
.PARAMETER InstallService
    Install and start the Windows service
    
.PARAMETER AutoStartTray
    Configure tray app to start automatically at Windows login
    
.PARAMETER InstallPath
    Installation directory (default: C:\Program Files\BlobStorageDriver)
    
.EXAMPLE
    .\Install.ps1 -Install -InstallService -AutoStartTray
    
.EXAMPLE
    .\Install.ps1 -Uninstall
#>

[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(ParameterSetName = 'Install')]
    [switch]$Install,
    
    [Parameter(ParameterSetName = 'Uninstall')]
    [switch]$Uninstall,
    
    [Parameter(ParameterSetName = 'Install')]
    [switch]$InstallService,
    
    [Parameter(ParameterSetName = 'Install')]
    [switch]$AutoStartTray,
    
    [Parameter(ParameterSetName = 'Install')]
    [string]$InstallPath = "$env:ProgramFiles\BlobStorageDriver"
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'BlobStorageDriver'
$AppName = 'Blob Storage Driver'
$TrayAppExe = 'BlobStorageDriver.WinUI.exe'
$ServiceExe = 'BlobStorageDriver.Service.exe'
$RegistryRunKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'

function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Write-Header {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Blob Storage Driver Installer" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
}

function Install-Application {
    Write-Host "Installing Blob Storage Driver..." -ForegroundColor Green
    
    # Create installation directory
    if (!(Test-Path $InstallPath)) {
        New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
        Write-Host "  Created installation directory: $InstallPath" -ForegroundColor Gray
    }
    
    $TrayAppPath = Join-Path $InstallPath "TrayApp"
    $ServicePath = Join-Path $InstallPath "Service"
    
    New-Item -ItemType Directory -Path $TrayAppPath -Force | Out-Null
    New-Item -ItemType Directory -Path $ServicePath -Force | Out-Null
    
    # Find publish output
    $ScriptDir = Split-Path -Parent $MyInvocation.ScriptName
    $SrcDir = Split-Path -Parent $ScriptDir
    
    $WinUIPublish = Join-Path $SrcDir "BlobStorageDriver.WinUI\bin\Release\net10.0-windows10.0.22621.0\win-x64\publish"
    $ServicePublish = Join-Path $SrcDir "BlobStorageDriver.Service\bin\Release\net10.0-windows10.0.22621.0\win-x64\publish"
    
    # Check if published files exist
    if (!(Test-Path $WinUIPublish)) {
        $WinUIPublish = Join-Path $SrcDir "BlobStorageDriver.WinUI\bin\Release\net10.0-windows10.0.22621.0"
    }
    if (!(Test-Path $ServicePublish)) {
        $ServicePublish = Join-Path $SrcDir "BlobStorageDriver.Service\bin\Release\net10.0-windows10.0.22621.0"
    }
    
    # Copy Tray App files
    Write-Host "  Copying Tray Application files..." -ForegroundColor Gray
    if (Test-Path $WinUIPublish) {
        Copy-Item -Path "$WinUIPublish\*" -Destination $TrayAppPath -Recurse -Force
    } else {
        Write-Warning "Tray App publish folder not found. Please build with: dotnet publish -c Release"
    }
    
    # Copy Service files
    Write-Host "  Copying Service files..." -ForegroundColor Gray
    if (Test-Path $ServicePublish) {
        Copy-Item -Path "$ServicePublish\*" -Destination $ServicePath -Recurse -Force
    } else {
        Write-Warning "Service publish folder not found. Please build with: dotnet publish -c Release"
    }
    
    # Create Start Menu shortcuts
    Write-Host "  Creating Start Menu shortcuts..." -ForegroundColor Gray
    $StartMenuPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\$AppName"
    New-Item -ItemType Directory -Path $StartMenuPath -Force | Out-Null
    
    $WshShell = New-Object -ComObject WScript.Shell
    
    # Main app shortcut
    $Shortcut = $WshShell.CreateShortcut("$StartMenuPath\Blob Storage Driver.lnk")
    $Shortcut.TargetPath = Join-Path $TrayAppPath $TrayAppExe
    $Shortcut.WorkingDirectory = $TrayAppPath
    $Shortcut.Description = "Azure Blob Storage file system driver"
    $Shortcut.Save()
    
    # Uninstall shortcut
    $UninstallShortcut = $WshShell.CreateShortcut("$StartMenuPath\Uninstall.lnk")
    $UninstallShortcut.TargetPath = "powershell.exe"
    $UninstallShortcut.Arguments = "-ExecutionPolicy Bypass -File `"$ScriptDir\Install.ps1`" -Uninstall"
    $UninstallShortcut.Description = "Uninstall Blob Storage Driver"
    $UninstallShortcut.Save()
    
    Write-Host "  Application installed successfully!" -ForegroundColor Green
    
    # Install Windows Service if requested
    if ($InstallService) {
        Install-WindowsService
    }
    
    # Configure auto-start if requested
    if ($AutoStartTray) {
        Enable-AutoStart
    }
    
    Write-Host ""
    Write-Host "Installation complete!" -ForegroundColor Green
    Write-Host ""
    
    # Ask to launch
    $launch = Read-Host "Would you like to launch Blob Storage Driver now? (Y/n)"
    if ($launch -ne 'n' -and $launch -ne 'N') {
        Start-Process -FilePath (Join-Path $TrayAppPath $TrayAppExe) -ArgumentList "--minimized"
        Write-Host "Application launched (minimized to system tray)" -ForegroundColor Cyan
    }
}

function Install-WindowsService {
    Write-Host ""
    Write-Host "Installing Windows Service..." -ForegroundColor Green
    
    $ServicePath = Join-Path $InstallPath "Service"
    $ServiceExePath = Join-Path $ServicePath $ServiceExe
    
    if (!(Test-Path $ServiceExePath)) {
        Write-Warning "Service executable not found at: $ServiceExePath"
        return
    }
    
    # Stop existing service if running
    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existingService) {
        Write-Host "  Stopping existing service..." -ForegroundColor Gray
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        
        Write-Host "  Removing existing service..." -ForegroundColor Gray
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }
    
    # Install new service
    Write-Host "  Creating Windows service..." -ForegroundColor Gray
    New-Service -Name $ServiceName `
                -BinaryPathName $ServiceExePath `
                -DisplayName "Blob Storage Driver Service" `
                -Description "Background synchronization service for Azure Blob Storage" `
                -StartupType Automatic | Out-Null
    
    Write-Host "  Starting service..." -ForegroundColor Gray
    Start-Service -Name $ServiceName
    
    Write-Host "  Windows Service installed and started!" -ForegroundColor Green
}

function Enable-AutoStart {
    Write-Host ""
    Write-Host "Configuring auto-start at login..." -ForegroundColor Green
    
    $TrayAppPath = Join-Path $InstallPath "TrayApp"
    $TrayExePath = Join-Path $TrayAppPath $TrayAppExe
    
    if (!(Test-Path $TrayExePath)) {
        Write-Warning "Tray app executable not found at: $TrayExePath"
        return
    }
    
    Set-ItemProperty -Path $RegistryRunKey `
                     -Name $ServiceName `
                     -Value "`"$TrayExePath`" --minimized"
    
    Write-Host "  Auto-start configured!" -ForegroundColor Green
}

function Uninstall-Application {
    Write-Host "Uninstalling Blob Storage Driver..." -ForegroundColor Yellow
    
    # Stop and remove service
    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existingService) {
        Write-Host "  Stopping Windows service..." -ForegroundColor Gray
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        
        Write-Host "  Removing Windows service..." -ForegroundColor Gray
        sc.exe delete $ServiceName | Out-Null
    }
    
    # Stop tray app
    Write-Host "  Stopping tray application..." -ForegroundColor Gray
    Get-Process -Name "BlobStorageDriver.WinUI" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
    
    # Remove auto-start registry entry
    Write-Host "  Removing auto-start configuration..." -ForegroundColor Gray
    Remove-ItemProperty -Path $RegistryRunKey -Name $ServiceName -ErrorAction SilentlyContinue
    
    # Remove Start Menu shortcuts
    Write-Host "  Removing Start Menu shortcuts..." -ForegroundColor Gray
    $StartMenuPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\$AppName"
    if (Test-Path $StartMenuPath) {
        Remove-Item -Path $StartMenuPath -Recurse -Force
    }
    
    # Remove installation directory
    Write-Host "  Removing application files..." -ForegroundColor Gray
    if (Test-Path $InstallPath) {
        Remove-Item -Path $InstallPath -Recurse -Force
    }
    
    Write-Host ""
    Write-Host "Uninstallation complete!" -ForegroundColor Green
}

# Main execution
Write-Header

if (!(Test-Administrator)) {
    Write-Host "This installer requires administrator privileges." -ForegroundColor Red
    Write-Host "Please run PowerShell as Administrator and try again." -ForegroundColor Yellow
    exit 1
}

if ($Uninstall) {
    Uninstall-Application
} else {
    Install-Application
}
