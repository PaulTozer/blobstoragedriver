using BlobStorageDriver.Common.Models;

namespace BlobStorageDriver.Common.Events;

/// <summary>
/// Event arguments for file state changes
/// </summary>
public class FileStateChangedEventArgs : EventArgs
{
    public CloudFileItem File { get; }
    public FileState OldState { get; }
    public FileState NewState { get; }
    
    public FileStateChangedEventArgs(CloudFileItem file, FileState oldState, FileState newState)
    {
        File = file;
        OldState = oldState;
        NewState = newState;
    }
}

/// <summary>
/// Event arguments for sync progress updates
/// </summary>
public class SyncProgressEventArgs : EventArgs
{
    public SyncProgress Progress { get; }
    
    public SyncProgressEventArgs(SyncProgress progress)
    {
        Progress = progress;
    }
}

/// <summary>
/// Event arguments for conflict detection
/// </summary>
public class ConflictDetectedEventArgs : EventArgs
{
    public SyncConflict Conflict { get; }
    
    public ConflictDetectedEventArgs(SyncConflict conflict)
    {
        Conflict = conflict;
    }
}

/// <summary>
/// Event arguments for transfer progress
/// </summary>
public class TransferProgressEventArgs : EventArgs
{
    public string FilePath { get; }
    public long BytesTransferred { get; }
    public long TotalBytes { get; }
    public bool IsUpload { get; }
    
    public double ProgressPercentage => TotalBytes > 0 
        ? (double)BytesTransferred / TotalBytes * 100 
        : 0;
        
    public TransferProgressEventArgs(string filePath, long bytesTransferred, long totalBytes, bool isUpload)
    {
        FilePath = filePath;
        BytesTransferred = bytesTransferred;
        TotalBytes = totalBytes;
        IsUpload = isUpload;
    }
}

/// <summary>
/// Event arguments for sync errors
/// </summary>
public class SyncErrorEventArgs : EventArgs
{
    public string FilePath { get; }
    public string ErrorMessage { get; }
    public Exception? Exception { get; }
    public bool IsRecoverable { get; }
    
    public SyncErrorEventArgs(string filePath, string errorMessage, Exception? exception = null, bool isRecoverable = true)
    {
        FilePath = filePath;
        ErrorMessage = errorMessage;
        Exception = exception;
        IsRecoverable = isRecoverable;
    }
}
