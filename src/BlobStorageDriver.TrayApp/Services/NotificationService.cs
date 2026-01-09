using System.IO;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.Common.Models;
using BlobStorageDriver.SyncEngine;
using System.Windows;
using Windows.UI.Notifications;
using Microsoft.Toolkit.Uwp.Notifications;
using Application = System.Windows.Application;

namespace BlobStorageDriver.TrayApp.Services;

/// <summary>
/// Service for showing Windows notifications
/// </summary>
public class NotificationService
{
    private readonly FileSyncEngine _syncEngine;
    private readonly UiSettings _uiSettings;
    private const string AppId = "BlobStorageDriver";

    public NotificationService(FileSyncEngine syncEngine, UiSettings uiSettings)
    {
        _syncEngine = syncEngine;
        _uiSettings = uiSettings;
        
        _syncEngine.ConflictDetected += OnConflictDetected;
        _syncEngine.SyncError += OnSyncError;
    }

    private void OnConflictDetected(object? sender, Common.Events.ConflictDetectedEventArgs e)
    {
        if (_uiSettings.ShowConflictNotifications)
        {
            ShowConflictNotification(e.Conflict);
        }
    }

    private void OnSyncError(object? sender, Common.Events.SyncErrorEventArgs e)
    {
        if (_uiSettings.ShowNotifications && !e.IsRecoverable)
        {
            ShowErrorNotification(e.FilePath, e.ErrorMessage);
        }
    }

    public void ShowConflictNotification(SyncConflict conflict)
    {
        try
        {
            new ToastContentBuilder()
                .AddText("Sync Conflict Detected")
                .AddText($"File '{conflict.FileName}' has conflicting changes.")
                .AddText("Click to resolve the conflict.")
                .AddArgument("action", "openConflicts")
                .AddArgument("filePath", conflict.FilePath)
                .AddButton(new ToastButton()
                    .SetContent("Keep Local")
                    .AddArgument("resolution", "local"))
                .AddButton(new ToastButton()
                    .SetContent("Keep Cloud")
                    .AddArgument("resolution", "cloud"))
                .Show();
        }
        catch
        {
            // Fall back to standard notification
            ShowStandardNotification("Sync Conflict", $"'{conflict.FileName}' has conflicts.");
        }
    }

    public void ShowErrorNotification(string filePath, string errorMessage)
    {
        try
        {
            new ToastContentBuilder()
                .AddText("Sync Error")
                .AddText($"Failed to sync '{Path.GetFileName(filePath)}'")
                .AddText(errorMessage)
                .AddArgument("action", "openActivity")
                .Show();
        }
        catch
        {
            ShowStandardNotification("Sync Error", errorMessage);
        }
    }

    public void ShowSyncCompleteNotification(int uploadedFiles, int downloadedFiles)
    {
        if (!_uiSettings.ShowNotifications) return;

        try
        {
            var message = uploadedFiles > 0 || downloadedFiles > 0
                ? $"Uploaded {uploadedFiles} files, downloaded {downloadedFiles} files"
                : "All files are up to date";

            new ToastContentBuilder()
                .AddText("Sync Complete")
                .AddText(message)
                .Show();
        }
        catch
        {
            // Ignore notification errors
        }
    }

    public void ShowOfflineNotification()
    {
        if (!_uiSettings.ShowNotifications) return;

        try
        {
            new ToastContentBuilder()
                .AddText("Working Offline")
                .AddText("Internet connection lost. Changes will sync when connection is restored.")
                .Show();
        }
        catch
        {
            // Ignore notification errors
        }
    }

    public void ShowOnlineNotification()
    {
        if (!_uiSettings.ShowNotifications) return;

        try
        {
            new ToastContentBuilder()
                .AddText("Back Online")
                .AddText("Syncing changes...")
                .Show();
        }
        catch
        {
            // Ignore notification errors
        }
    }

    private void ShowStandardNotification(string title, string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }
}
