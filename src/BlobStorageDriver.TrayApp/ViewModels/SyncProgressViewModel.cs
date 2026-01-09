using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlobStorageDriver.Common.Models;
using BlobStorageDriver.SyncEngine;
using System.Collections.ObjectModel;

namespace BlobStorageDriver.TrayApp.ViewModels;

public partial class SyncProgressViewModel : ObservableObject
{
    private readonly FileSyncEngine _syncEngine;

    [ObservableProperty]
    private SyncState _syncState;

    [ObservableProperty]
    private int _totalFiles;

    [ObservableProperty]
    private int _processedFiles;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private long _processedBytes;

    [ObservableProperty]
    private int _uploadingCount;

    [ObservableProperty]
    private int _downloadingCount;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private string _currentFile = string.Empty;

    [ObservableProperty]
    private TimeSpan? _estimatedTimeRemaining;

    public ObservableCollection<FileTransferItem> ActiveTransfers { get; } = new();

    public SyncProgressViewModel(FileSyncEngine syncEngine)
    {
        _syncEngine = syncEngine;
        
        _syncEngine.ProgressChanged += OnProgressChanged;
        _syncEngine.TransferProgress += OnTransferProgress;
    }

    private void OnProgressChanged(object? sender, Common.Events.SyncProgressEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            SyncState = e.Progress.State;
            TotalFiles = e.Progress.TotalFiles;
            ProcessedFiles = e.Progress.ProcessedFiles;
            TotalBytes = e.Progress.TotalBytes;
            ProcessedBytes = e.Progress.ProcessedBytes;
            UploadingCount = e.Progress.UploadingCount;
            DownloadingCount = e.Progress.DownloadingCount;
            ProgressPercentage = e.Progress.ProgressPercentage;
            CurrentFile = e.Progress.CurrentFile ?? string.Empty;
            EstimatedTimeRemaining = e.Progress.EstimatedTimeRemaining;
        });
    }

    private void OnTransferProgress(object? sender, Common.Events.TransferProgressEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var existing = ActiveTransfers.FirstOrDefault(t => t.FilePath == e.FilePath);
            
            if (existing != null)
            {
                existing.BytesTransferred = e.BytesTransferred;
                existing.TotalBytes = e.TotalBytes;
                existing.ProgressPercentage = e.ProgressPercentage;
                
                if (e.BytesTransferred >= e.TotalBytes)
                {
                    ActiveTransfers.Remove(existing);
                }
            }
            else
            {
                ActiveTransfers.Add(new FileTransferItem
                {
                    FileName = Path.GetFileName(e.FilePath),
                    FilePath = e.FilePath,
                    IsUpload = e.IsUpload,
                    BytesTransferred = e.BytesTransferred,
                    TotalBytes = e.TotalBytes,
                    ProgressPercentage = e.ProgressPercentage
                });
            }
        });
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        await _syncEngine.PerformSyncAsync();
    }

    [RelayCommand]
    private void PauseSync()
    {
        _syncEngine.Pause();
    }

    [RelayCommand]
    private void ResumeSync()
    {
        _syncEngine.Resume();
    }

    public string FormattedEta => EstimatedTimeRemaining.HasValue
        ? $"{EstimatedTimeRemaining.Value:hh\\:mm\\:ss} remaining"
        : "Calculating...";

    public string FormattedProgress => $"{FormatBytes(ProcessedBytes)} / {FormatBytes(TotalBytes)}";

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        
        return $"{size:0.##} {sizes[order]}";
    }
}

public partial class FileTransferItem : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private bool _isUpload;

    [ObservableProperty]
    private long _bytesTransferred;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private double _progressPercentage;

    public string Direction => IsUpload ? "↑" : "↓";
}
