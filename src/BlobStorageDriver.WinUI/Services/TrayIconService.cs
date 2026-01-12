using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Drawing;
using System.Windows.Input;

namespace BlobStorageDriver.WinUI.Services;

/// <summary>
/// Tray icon service for WinUI 3 using H.NotifyIcon.
/// </summary>
public class TrayIconService : IDisposable
{
    private TaskbarIcon? _taskbarIcon;
    private bool _disposed;
    private Window? _mainWindow;
    
    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? SyncNowRequested;
    public event EventHandler? PauseRequested;
    
    public void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow;
        
        // Create the tray icon
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Blob Storage Driver",
            NoLeftClickDelay = true
        };
        
        // Generate icon
        _taskbarIcon.Icon = CreateStorageIcon();
        
        // Create context menu
        _taskbarIcon.ContextFlyout = CreateContextMenu();
        
        // Handle double-click to show window using DoubleClickCommand
        _taskbarIcon.DoubleClickCommand = new RelayCommand(RequestShow);
        
        // Show the tray icon
        _taskbarIcon.ForceCreate();
    }
    
    private Icon CreateStorageIcon()
    {
        // Create a simple 16x16 icon with storage-like appearance
        var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);
        
        // Fill with Azure blue color
        using var brush = new SolidBrush(Color.FromArgb(0, 120, 212));
        using var lightBrush = new SolidBrush(Color.FromArgb(80, 200, 230));
        
        // Draw stacked cylinders (simplified storage icon)
        g.FillEllipse(brush, 1, 10, 14, 4);
        g.FillRectangle(brush, 1, 7, 14, 5);
        g.FillEllipse(lightBrush, 1, 5, 14, 4);
        g.FillRectangle(brush, 1, 2, 14, 5);
        g.FillEllipse(lightBrush, 1, 0, 14, 4);
        
        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }
    
    private Microsoft.UI.Xaml.Controls.MenuFlyout CreateContextMenu()
    {
        var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();
        
        var openItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Open Blob Storage Driver" };
        openItem.Click += (s, e) => RequestShow();
        menu.Items.Add(openItem);
        
        menu.Items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());
        
        var syncNowItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Sync Now" };
        syncNowItem.Click += (s, e) => SyncNowRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(syncNowItem);
        
        var pauseItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Pause Sync" };
        pauseItem.Click += (s, e) => PauseRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(pauseItem);
        
        menu.Items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());
        
        var exitItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += (s, e) => RequestExit();
        menu.Items.Add(exitItem);
        
        return menu;
    }
    
    public void UpdateTooltip(string text)
    {
        if (_taskbarIcon != null)
        {
            _taskbarIcon.ToolTipText = text;
        }
    }
    
    public void ShowBalloon(string title, string message)
    {
        _taskbarIcon?.ShowNotification(title, message);
    }
    
    public void RequestShow()
    {
        ShowRequested?.Invoke(this, EventArgs.Empty);
        if (_mainWindow != null)
        {
            _mainWindow.Activate();
        }
    }
    
    public void RequestExit()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _taskbarIcon?.Dispose();
            _taskbarIcon = null;
            _disposed = true;
        }
    }
}

/// <summary>
/// Simple relay command implementation.
/// </summary>
internal class RelayCommand : ICommand
{
    private readonly Action _execute;
    
    public RelayCommand(Action execute) => _execute = execute;
    
#pragma warning disable CS0067 // Event is never used - required by ICommand interface
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
    
    public bool CanExecute(object? parameter) => true;
    
    public void Execute(object? parameter) => _execute();
}
