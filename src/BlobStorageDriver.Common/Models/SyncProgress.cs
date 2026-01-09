namespace BlobStorageDriver.Common.Models;

/// <summary>
/// Represents the progress of a sync operation
/// </summary>
public class SyncProgress
{
    public SyncState State { get; set; } = SyncState.Idle;
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public long TotalBytes { get; set; }
    public long ProcessedBytes { get; set; }
    public int UploadingCount { get; set; }
    public int DownloadingCount { get; set; }
    public int PendingCount { get; set; }
    public int ErrorCount { get; set; }
    public int ConflictCount { get; set; }
    public string? CurrentFile { get; set; }
    public string? CurrentOperation { get; set; }
    public DateTime LastSyncTime { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    
    public double ProgressPercentage => TotalBytes > 0 
        ? (double)ProcessedBytes / TotalBytes * 100 
        : TotalFiles > 0 
            ? (double)ProcessedFiles / TotalFiles * 100 
            : 0;
            
    public bool IsSyncing => State == SyncState.Syncing;
    public bool HasErrors => ErrorCount > 0;
    public bool HasConflicts => ConflictCount > 0;
}

/// <summary>
/// Overall sync state
/// </summary>
public enum SyncState
{
    Idle,
    Syncing,
    Paused,
    Offline,
    Error,
    Initializing
}
