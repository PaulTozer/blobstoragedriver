namespace BlobStorageDriver.Common.Models;

/// <summary>
/// Represents a file or folder item in the cloud storage
/// </summary>
public class CloudFileItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string? ETag { get; set; }
    public string? ContentHash { get; set; }
    public FileState State { get; set; } = FileState.CloudOnly;
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Synced;
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Represents the state of a file in relation to cloud and local storage
/// </summary>
public enum FileState
{
    /// <summary>File exists only in cloud, shown as placeholder locally</summary>
    CloudOnly,
    
    /// <summary>File is cached locally and synced with cloud</summary>
    Synced,
    
    /// <summary>File is being downloaded from cloud</summary>
    Downloading,
    
    /// <summary>File is being uploaded to cloud</summary>
    Uploading,
    
    /// <summary>File exists only locally (pending upload)</summary>
    LocalOnly,
    
    /// <summary>File has local changes pending sync</summary>
    LocallyModified,
    
    /// <summary>File was deleted locally, pending cloud deletion</summary>
    PendingDelete,
    
    /// <summary>File has sync conflict</summary>
    Conflict,
    
    /// <summary>File sync failed with error</summary>
    Error
}

/// <summary>
/// Represents the sync status of a file
/// </summary>
public enum SyncStatus
{
    Synced,
    Syncing,
    Pending,
    Paused,
    Error,
    Conflict
}
