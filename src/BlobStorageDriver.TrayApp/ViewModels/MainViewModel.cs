using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlobStorageDriver.Common.Models;
using BlobStorageDriver.SyncEngine;
using BlobStorageDriver.SyncEngine.Integration;
using System.Collections.ObjectModel;

namespace BlobStorageDriver.TrayApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private FileSyncEngine? _syncEngine;

    [ObservableProperty]
    private SyncState _syncState = SyncState.Idle;

    [ObservableProperty]
    private string _statusMessage = "Not configured - Go to Settings to configure Azure connection";

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private int _conflictCount;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private string _currentFile = string.Empty;

    [ObservableProperty]
    private DateTime _lastSyncTime;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private long _uploadedBytes;

    [ObservableProperty]
    private long _downloadedBytes;

    [ObservableProperty]
    private int _pendingCount;
    
    [ObservableProperty]
    private bool _isConnected;

    public ObservableCollection<SyncActivityItem> RecentActivity { get; } = new();
    public ObservableCollection<SyncConflict> Conflicts { get; } = new();

    public MainViewModel()
    {
        // Default constructor - sync engine will be set later when configured
    }

    public MainViewModel(FileSyncEngine syncEngine)
    {
        SetSyncEngine(syncEngine);
    }

    public void SetSyncEngine(FileSyncEngine syncEngine)
    {
        _syncEngine = syncEngine;

        _syncEngine.ProgressChanged += OnProgressChanged;
        _syncEngine.FileStateChanged += OnFileStateChanged;
        _syncEngine.ConflictDetected += OnConflictDetected;
        _syncEngine.SyncError += OnSyncError;
        _syncEngine.TransferProgress += OnTransferProgress;
        
        StatusMessage = "Ready";
    }
    
    /// <summary>
    /// Add activity from the IntegrationManager
    /// </summary>
    public void AddActivity(FileActivityEventArgs e)
    {
        var icon = e.ActivityType switch
        {
            FileActivityType.Created => "📄",
            FileActivityType.Modified => "✏️",
            FileActivityType.Deleted => "🗑️",
            FileActivityType.Renamed => "📝",
            FileActivityType.Uploading => "⬆️",
            FileActivityType.Uploaded => "✅",
            FileActivityType.Downloading => "⬇️",
            FileActivityType.Downloaded => "✅",
            FileActivityType.Error => "❌",
            _ => "📋"
        };
        
        var activity = new SyncActivityItem
        {
            Icon = icon,
            FileName = Path.GetFileName(e.RelativePath),
            Status = e.Message,
            Time = e.Timestamp
        };
        
        RecentActivity.Insert(0, activity);
        
        // Keep only last 100 items
        while (RecentActivity.Count > 100)
        {
            RecentActivity.RemoveAt(RecentActivity.Count - 1);
        }
        
        // Update current file display
        if (e.ActivityType == FileActivityType.Uploading || e.ActivityType == FileActivityType.Downloading)
        {
            CurrentFile = e.RelativePath;
        }
        
        // Update last sync time on successful upload/download
        if (e.ActivityType == FileActivityType.Uploaded || e.ActivityType == FileActivityType.Downloaded)
        {
            LastSyncTime = DateTime.Now;
        }
    }

    private void OnProgressChanged(object? sender, Common.Events.SyncProgressEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            SyncState = e.Progress.State;
            ProgressPercentage = e.Progress.ProgressPercentage;
            ConflictCount = e.Progress.ConflictCount;
            ErrorCount = e.Progress.ErrorCount;
            CurrentFile = e.Progress.CurrentFile ?? string.Empty;
            LastSyncTime = e.Progress.LastSyncTime;
            PendingCount = e.Progress.PendingCount;

            StatusMessage = e.Progress.State switch
            {
                SyncState.Idle => "Up to date",
                SyncState.Syncing => $"Syncing... {e.Progress.ProgressPercentage:F0}%",
                SyncState.Paused => "Sync paused",
                SyncState.Offline => "Offline",
                SyncState.Error => "Sync error occurred",
                SyncState.Initializing => "Initializing...",
                _ => "Ready"
            };
        });
    }

    private void OnFileStateChanged(object? sender, Common.Events.FileStateChangedEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var activity = new SyncActivityItem
            {
                FileName = e.File.Name,
                FilePath = e.File.RelativePath,
                Action = GetActionDescription(e.OldState, e.NewState),
                Timestamp = DateTime.Now,
                State = e.NewState
            };

            RecentActivity.Insert(0, activity);

            // Keep only last 50 items
            while (RecentActivity.Count > 50)
            {
                RecentActivity.RemoveAt(RecentActivity.Count - 1);
            }
        });
    }

    private static string GetActionDescription(FileState oldState, FileState newState)
    {
        return (oldState, newState) switch
        {
            (_, FileState.Synced) when oldState == FileState.Uploading => "Uploaded",
            (_, FileState.Synced) when oldState == FileState.Downloading => "Downloaded",
            (_, FileState.Uploading) => "Uploading",
            (_, FileState.Downloading) => "Downloading",
            (_, FileState.Conflict) => "Conflict detected",
            (_, FileState.Error) => "Error",
            (_, FileState.LocallyModified) => "Modified locally",
            (_, FileState.PendingDelete) => "Pending deletion",
            _ => "Updated"
        };
    }

    private void OnConflictDetected(object? sender, Common.Events.ConflictDetectedEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Conflicts.Add(e.Conflict);
        });
    }

    private void OnSyncError(object? sender, Common.Events.SyncErrorEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var activity = new SyncActivityItem
            {
                FileName = Path.GetFileName(e.FilePath),
                FilePath = e.FilePath,
                Action = $"Error: {e.ErrorMessage}",
                Timestamp = DateTime.Now,
                State = FileState.Error,
                IsError = true
            };

            RecentActivity.Insert(0, activity);
        });
    }

    private void OnTransferProgress(object? sender, Common.Events.TransferProgressEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            if (e.IsUpload)
            {
                UploadedBytes = e.BytesTransferred;
            }
            else
            {
                DownloadedBytes = e.BytesTransferred;
            }
        });
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (_syncEngine == null)
        {
            StatusMessage = "Not configured - Go to Settings to configure Azure connection";
            return;
        }
        await _syncEngine.PerformSyncAsync();
    }

    [RelayCommand]
    private void PauseSync()
    {
        if (_syncEngine == null) return;
        
        if (IsPaused)
        {
            _syncEngine.Resume();
            IsPaused = false;
        }
        else
        {
            _syncEngine.Pause();
            IsPaused = true;
        }
    }

    [RelayCommand]
    private void OpenSyncFolder()
    {
        var config = App.Services.GetService(typeof(Common.Configuration.CacheSettings)) 
            as Common.Configuration.CacheSettings;
        
        if (config != null && Directory.Exists(config.LocalSyncFolder))
        {
            System.Diagnostics.Process.Start("explorer.exe", config.LocalSyncFolder);
        }
    }

    [RelayCommand]
    private async Task ResolveConflictAsync(SyncConflict conflict)
    {
        if (_syncEngine == null) return;
        
        // This will be called from the conflict resolution UI
        if (conflict.Resolution.HasValue)
        {
            await _syncEngine.ResolveConflictAsync(conflict.FilePath, conflict.Resolution.Value);
            Conflicts.Remove(conflict);
        }
    }
}

public class SyncActivityItem
{
    public string Icon { get; set; } = "📋";
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public DateTime Time { get; set; }
    public FileState State { get; set; }
    public bool IsError { get; set; }
}
