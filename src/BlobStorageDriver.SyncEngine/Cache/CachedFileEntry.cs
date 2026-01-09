using BlobStorageDriver.Common.Models;

namespace BlobStorageDriver.SyncEngine.Cache;

/// <summary>
/// Cached file entry stored in the local database
/// </summary>
public class CachedFileEntry
{
    public string Id { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LocalModifiedAt { get; set; }
    public DateTime CloudModifiedAt { get; set; }
    public string? CloudETag { get; set; }
    public string? LocalContentHash { get; set; }
    public string? CloudContentHash { get; set; }
    public FileState State { get; set; }
    public SyncStatus SyncStatus { get; set; }
    public DateTime LastAccessedAt { get; set; }
    public DateTime LastSyncedAt { get; set; }
    public bool IsPinned { get; set; }
    public bool IsHydrated { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Represents a pending sync operation
/// </summary>
public class PendingSyncOperation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RelativePath { get; set; } = string.Empty;
    public SyncOperationType OperationType { get; set; }
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public int Priority { get; set; }
    public int RetryCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Type of sync operation
/// </summary>
public enum SyncOperationType
{
    Upload,
    Download,
    Delete,
    Move,
    CreateDirectory,
    UpdateMetadata
}
