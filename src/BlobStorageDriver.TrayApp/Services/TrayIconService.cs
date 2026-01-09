using System.IO;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.Common.Models;
using BlobStorageDriver.SyncEngine;
using Hardcodet.Wpf.TaskbarNotification;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;

namespace BlobStorageDriver.TrayApp.Services;

/// <summary>
/// Service for managing the system tray icon
/// </summary>
public class TrayIconService
{
    private readonly FileSyncEngine _syncEngine;
    private readonly UiSettings _uiSettings;
    private TaskbarIcon? _taskbarIcon;
    
    private readonly BitmapImage _iconIdle;
    private readonly BitmapImage _iconSyncing;
    private readonly BitmapImage _iconPaused;
    private readonly BitmapImage _iconError;
    private readonly BitmapImage _iconConflict;

    public TrayIconService(FileSyncEngine syncEngine, UiSettings uiSettings)
    {
        _syncEngine = syncEngine;
        _uiSettings = uiSettings;
        
        // Load icon resources
        _iconIdle = LoadIcon("Assets/tray-idle.png");
        _iconSyncing = LoadIcon("Assets/tray-syncing.png");
        _iconPaused = LoadIcon("Assets/tray-paused.png");
        _iconError = LoadIcon("Assets/tray-error.png");
        _iconConflict = LoadIcon("Assets/tray-conflict.png");
        
        _syncEngine.ProgressChanged += OnProgressChanged;
        _syncEngine.ConflictDetected += OnConflictDetected;
        _syncEngine.SyncError += OnSyncError;
    }

    public void Initialize(TaskbarIcon taskbarIcon)
    {
        _taskbarIcon = taskbarIcon;
        UpdateIcon(SyncState.Idle);
    }

    private void OnProgressChanged(object? sender, Common.Events.SyncProgressEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            UpdateIcon(e.Progress.State);
            UpdateTooltip(e.Progress);
        });
    }

    private void OnConflictDetected(object? sender, Common.Events.ConflictDetectedEventArgs e)
    {
        if (_uiSettings.ShowConflictNotifications)
        {
            ShowNotification(
                "Sync Conflict Detected",
                $"File '{e.Conflict.FileName}' has conflicting changes.",
                BalloonIcon.Warning);
        }
    }

    private void OnSyncError(object? sender, Common.Events.SyncErrorEventArgs e)
    {
        if (_uiSettings.ShowNotifications)
        {
            ShowNotification(
                "Sync Error",
                $"Failed to sync '{Path.GetFileName(e.FilePath)}': {e.ErrorMessage}",
                BalloonIcon.Error);
        }
    }

    private void UpdateIcon(SyncState state)
    {
        if (_taskbarIcon == null) return;

        var icon = state switch
        {
            SyncState.Syncing => _iconSyncing,
            SyncState.Paused => _iconPaused,
            SyncState.Error => _iconError,
            _ when _syncEngine.Conflicts.Any() => _iconConflict,
            _ => _iconIdle
        };

        // Update icon source
        // Note: TaskbarIcon expects .ico files, so we'd need to convert or use actual .ico files
    }

    private void UpdateTooltip(SyncProgress progress)
    {
        if (_taskbarIcon == null) return;

        var tooltip = progress.State switch
        {
            SyncState.Idle => "Blob Storage Driver - Up to date",
            SyncState.Syncing => $"Blob Storage Driver - Syncing ({progress.ProgressPercentage:F0}%)",
            SyncState.Paused => "Blob Storage Driver - Paused",
            SyncState.Offline => "Blob Storage Driver - Offline",
            SyncState.Error => "Blob Storage Driver - Error",
            SyncState.Initializing => "Blob Storage Driver - Initializing...",
            _ => "Blob Storage Driver"
        };

        if (progress.ConflictCount > 0)
        {
            tooltip += $"\n{progress.ConflictCount} conflict(s) need attention";
        }

        _taskbarIcon.ToolTipText = tooltip;
    }

    public void ShowNotification(string title, string message, BalloonIcon icon = BalloonIcon.Info)
    {
        if (_taskbarIcon != null && _uiSettings.ShowNotifications)
        {
            _taskbarIcon.ShowBalloonTip(title, message, icon);
        }
    }

    private static BitmapImage LoadIcon(string path)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/{path}", UriKind.Absolute);
            return new BitmapImage(uri);
        }
        catch
        {
            return new BitmapImage();
        }
    }
}
