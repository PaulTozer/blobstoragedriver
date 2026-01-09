namespace BlobStorageDriver.Common.Models;

/// <summary>
/// Represents a file conflict between local and cloud versions
/// </summary>
public class SyncConflict
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public ConflictType Type { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    
    // Local version info
    public DateTime? LocalModifiedAt { get; set; }
    public long? LocalSize { get; set; }
    public string? LocalContentHash { get; set; }
    
    // Cloud version info
    public DateTime? CloudModifiedAt { get; set; }
    public long? CloudSize { get; set; }
    public string? CloudContentHash { get; set; }
    public string? CloudETag { get; set; }
    
    public ConflictResolution? Resolution { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public bool IsResolved => Resolution.HasValue;
}

/// <summary>
/// Type of sync conflict
/// </summary>
public enum ConflictType
{
    /// <summary>Both local and cloud versions were modified</summary>
    BothModified,
    
    /// <summary>File was modified locally but deleted in cloud</summary>
    LocalModifiedCloudDeleted,
    
    /// <summary>File was deleted locally but modified in cloud</summary>
    LocalDeletedCloudModified,
    
    /// <summary>File was created in both locations with same name</summary>
    DuplicateCreate,
    
    /// <summary>File type changed (file to folder or vice versa)</summary>
    TypeMismatch
}

/// <summary>
/// Resolution option for a sync conflict
/// </summary>
public enum ConflictResolution
{
    /// <summary>Keep the local version</summary>
    KeepLocal,
    
    /// <summary>Keep the cloud version</summary>
    KeepCloud,
    
    /// <summary>Keep both versions (rename local)</summary>
    KeepBoth,
    
    /// <summary>Merge changes (if possible)</summary>
    Merge,
    
    /// <summary>Delete both versions</summary>
    DeleteBoth
}
