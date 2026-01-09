using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.Common.Events;
using BlobStorageDriver.Common.Models;
using BlobStorageDriver.CloudProvider;
using BlobStorageDriver.SyncEngine.Cache;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BlobStorageDriver.SyncEngine;

/// <summary>
/// Main synchronization engine that coordinates between local cache and cloud storage
/// </summary>
public class FileSyncEngine : IDisposable
{
    private readonly ICloudStorageProvider _cloudProvider;
    private readonly LocalCacheManager _cacheManager;
    private readonly SyncSettings _syncSettings;
    private readonly ILogger<FileSyncEngine> _logger;
    
    private FileSystemWatcher? _fileWatcher;
    private Timer? _syncTimer;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTime> _pendingChanges = new();
    private readonly ConcurrentDictionary<string, SyncConflict> _conflicts = new();
    
    private SyncProgress _currentProgress = new();
    private bool _isDisposed;
    private bool _isPaused;
    
    // Events
    public event EventHandler<SyncProgressEventArgs>? ProgressChanged;
    public event EventHandler<FileStateChangedEventArgs>? FileStateChanged;
    public event EventHandler<ConflictDetectedEventArgs>? ConflictDetected;
    public event EventHandler<TransferProgressEventArgs>? TransferProgress;
    public event EventHandler<SyncErrorEventArgs>? SyncError;
    
    public SyncProgress CurrentProgress => _currentProgress;
    public bool IsPaused => _isPaused;
    public IReadOnlyDictionary<string, SyncConflict> Conflicts => _conflicts;

    public FileSyncEngine(
        ICloudStorageProvider cloudProvider,
        LocalCacheManager cacheManager,
        SyncSettings syncSettings,
        ILogger<FileSyncEngine> logger)
    {
        _cloudProvider = cloudProvider ?? throw new ArgumentNullException(nameof(cloudProvider));
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _syncSettings = syncSettings ?? throw new ArgumentNullException(nameof(syncSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public async Task StartAsync()
    {
        _logger.LogInformation("Starting sync engine...");
        
        _currentProgress.State = SyncState.Initializing;
        OnProgressChanged();
        
        // Test connection
        if (!await _cloudProvider.TestConnectionAsync(_cts.Token))
        {
            _currentProgress.State = SyncState.Error;
            OnProgressChanged();
            throw new InvalidOperationException("Failed to connect to cloud storage");
        }
        
        // Setup file watcher if enabled
        if (_syncSettings.EnableRealTimeSync)
        {
            SetupFileWatcher();
        }
        
        // Start periodic sync timer
        _syncTimer = new Timer(
            async _ => await PerformSyncAsync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(_syncSettings.SyncIntervalSeconds));
            
        _currentProgress.State = SyncState.Idle;
        OnProgressChanged();
        
        _logger.LogInformation("Sync engine started successfully");
    }
    
    public void Stop()
    {
        _logger.LogInformation("Stopping sync engine...");
        
        _syncTimer?.Dispose();
        _fileWatcher?.Dispose();
        _cts.Cancel();
        
        _currentProgress.State = SyncState.Idle;
        OnProgressChanged();
        
        _logger.LogInformation("Sync engine stopped");
    }
    
    public void Pause()
    {
        _isPaused = true;
        _currentProgress.State = SyncState.Paused;
        OnProgressChanged();
        _logger.LogInformation("Sync engine paused");
    }
    
    public void Resume()
    {
        _isPaused = false;
        _currentProgress.State = SyncState.Idle;
        OnProgressChanged();
        _logger.LogInformation("Sync engine resumed");
    }
    
    private void SetupFileWatcher()
    {
        var syncFolder = _cacheManager.GetLocalPath("");
        
        _fileWatcher = new FileSystemWatcher(syncFolder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                          NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };
        
        _fileWatcher.Created += OnFileCreated;
        _fileWatcher.Changed += OnFileChanged;
        _fileWatcher.Deleted += OnFileDeleted;
        _fileWatcher.Renamed += OnFileRenamed;
        _fileWatcher.Error += OnFileWatcherError;
        
        _logger.LogInformation("File watcher setup for: {Folder}", syncFolder);
    }
    
    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnoreFile(e.FullPath)) return;
        QueuePendingChange(e.FullPath, SyncOperationType.Upload);
    }
    
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnoreFile(e.FullPath)) return;
        QueuePendingChange(e.FullPath, SyncOperationType.Upload);
    }
    
    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnoreFile(e.FullPath)) return;
        QueuePendingChange(e.FullPath, SyncOperationType.Delete);
    }
    
    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (ShouldIgnoreFile(e.OldFullPath) && ShouldIgnoreFile(e.FullPath)) return;
        
        // Queue delete for old path and upload for new path
        QueuePendingChange(e.OldFullPath, SyncOperationType.Delete);
        QueuePendingChange(e.FullPath, SyncOperationType.Upload);
    }
    
    private void OnFileWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "File watcher error");
    }
    
    private bool ShouldIgnoreFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return _syncSettings.ExcludePatterns.Any(pattern => 
            MatchesWildcard(fileName, pattern));
    }
    
    private static bool MatchesWildcard(string fileName, string pattern)
    {
        if (pattern.StartsWith("*"))
            return fileName.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase);
        if (pattern.EndsWith("*"))
            return fileName.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase);
        return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
    
    private void QueuePendingChange(string fullPath, SyncOperationType operationType)
    {
        _pendingChanges[fullPath] = DateTime.UtcNow;
        
        // Debounce - process after delay
        _ = Task.Delay(_syncSettings.FileChangeDelayMs).ContinueWith(async _ =>
        {
            if (_pendingChanges.TryGetValue(fullPath, out var queuedTime))
            {
                if (DateTime.UtcNow - queuedTime >= TimeSpan.FromMilliseconds(_syncSettings.FileChangeDelayMs - 50))
                {
                    _pendingChanges.TryRemove(fullPath, out DateTime _);
                    await ProcessLocalChangeAsync(fullPath, operationType);
                }
            }
        });
    }
    
    private async Task ProcessLocalChangeAsync(string fullPath, SyncOperationType operationType)
    {
        if (_isPaused) return;
        
        try
        {
            var relativePath = _cacheManager.GetRelativePath(fullPath);
            
            var operation = new PendingSyncOperation
            {
                RelativePath = relativePath,
                OperationType = operationType,
                Priority = operationType == SyncOperationType.Delete ? 10 : 5
            };
            
            await _cacheManager.QueueOperationAsync(operation);
            
            _logger.LogDebug("Queued local change: {Type} for {Path}", operationType, relativePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process local change: {Path}", fullPath);
        }
    }
    
    public async Task PerformSyncAsync()
    {
        if (_isPaused || !await _syncLock.WaitAsync(0))
            return;
            
        try
        {
            _currentProgress.State = SyncState.Syncing;
            OnProgressChanged();
            
            // Process pending operations first
            await ProcessPendingOperationsAsync();
            
            // Then sync from cloud
            await SyncFromCloudAsync();
            
            // Enforce cache limits
            await _cacheManager.EnforceSpaceLimitAsync();
            
            _currentProgress.LastSyncTime = DateTime.UtcNow;
            _currentProgress.State = SyncState.Idle;
            OnProgressChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed");
            _currentProgress.State = SyncState.Error;
            OnProgressChanged();
        }
        finally
        {
            _syncLock.Release();
        }
    }
    
    private async Task ProcessPendingOperationsAsync()
    {
        var operations = await _cacheManager.GetPendingOperationsAsync();
        var uploadSemaphore = new SemaphoreSlim(_syncSettings.MaxConcurrentUploads);
        
        _currentProgress.PendingCount = operations.Count();
        OnProgressChanged();
        
        var tasks = operations.Select(async op =>
        {
            await uploadSemaphore.WaitAsync(_cts.Token);
            try
            {
                await ProcessOperationAsync(op);
            }
            finally
            {
                uploadSemaphore.Release();
            }
        });
        
        await Task.WhenAll(tasks);
    }
    
    private async Task ProcessOperationAsync(PendingSyncOperation operation)
    {
        try
        {
            switch (operation.OperationType)
            {
                case SyncOperationType.Upload:
                    await UploadFileAsync(operation.RelativePath);
                    break;
                case SyncOperationType.Download:
                    await DownloadFileAsync(operation.RelativePath);
                    break;
                case SyncOperationType.Delete:
                    await DeleteRemoteFileAsync(operation.RelativePath);
                    break;
                case SyncOperationType.CreateDirectory:
                    await _cloudProvider.CreateDirectoryAsync(operation.RelativePath, _cts.Token);
                    break;
            }
            
            await _cacheManager.CompleteOperationAsync(operation.Id);
            _currentProgress.ProcessedFiles++;
            OnProgressChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process operation: {Type} for {Path}", 
                operation.OperationType, operation.RelativePath);
                
            operation.RetryCount++;
            operation.LastAttemptAt = DateTime.UtcNow;
            operation.LastError = ex.Message;
            
            if (operation.RetryCount >= 3)
            {
                await _cacheManager.CompleteOperationAsync(operation.Id);
                _currentProgress.ErrorCount++;
                OnSyncError(operation.RelativePath, ex.Message, ex);
            }
            else
            {
                await _cacheManager.UpdateOperationAsync(operation);
            }
            
            OnProgressChanged();
        }
    }
    
    private async Task UploadFileAsync(string relativePath)
    {
        var localPath = _cacheManager.GetLocalPath(relativePath);
        
        if (!File.Exists(localPath))
        {
            _logger.LogWarning("File no longer exists for upload: {Path}", localPath);
            return;
        }
        
        var entry = await _cacheManager.GetEntryAsync(relativePath);
        var fileInfo = new FileInfo(localPath);
        
        // Check for conflicts
        if (entry != null && entry.CloudETag != null)
        {
            var cloudItem = await _cloudProvider.GetItemAsync(relativePath, _cts.Token);
            if (cloudItem != null && cloudItem.ETag != entry.CloudETag)
            {
                // Cloud file was modified - conflict!
                await HandleConflictAsync(relativePath, entry, cloudItem);
                return;
            }
        }
        
        // Update state to uploading
        if (entry != null)
        {
            var oldState = entry.State;
            entry.State = FileState.Uploading;
            await _cacheManager.AddOrUpdateEntryAsync(entry);
            OnFileStateChanged(entry, oldState, FileState.Uploading);
        }
        
        _currentProgress.UploadingCount++;
        _currentProgress.CurrentFile = relativePath;
        _currentProgress.CurrentOperation = "Uploading";
        OnProgressChanged();
        
        try
        {
            var progress = new Progress<long>(bytes =>
            {
                OnTransferProgress(relativePath, bytes, fileInfo.Length, true);
            });
            
            var cloudItem = await _cloudProvider.UploadFileAsync(localPath, relativePath, progress, _cts.Token);
            
            // Update cache entry
            var newEntry = new CachedFileEntry
            {
                RelativePath = relativePath,
                LocalPath = localPath,
                FileName = Path.GetFileName(localPath),
                IsDirectory = false,
                Size = fileInfo.Length,
                LocalModifiedAt = fileInfo.LastWriteTimeUtc,
                CloudModifiedAt = cloudItem.ModifiedAt,
                CloudETag = cloudItem.ETag,
                LocalContentHash = await _cacheManager.ComputeFileHashAsync(localPath),
                CloudContentHash = cloudItem.ContentHash,
                State = FileState.Synced,
                SyncStatus = SyncStatus.Synced,
                LastAccessedAt = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow,
                IsHydrated = true
            };
            
            await _cacheManager.AddOrUpdateEntryAsync(newEntry);
            OnFileStateChanged(newEntry, FileState.Uploading, FileState.Synced);
            
            _logger.LogInformation("Uploaded file: {Path}", relativePath);
        }
        finally
        {
            _currentProgress.UploadingCount--;
            OnProgressChanged();
        }
    }
    
    private async Task DownloadFileAsync(string relativePath)
    {
        var localPath = _cacheManager.GetLocalPath(relativePath);
        var entry = await _cacheManager.GetEntryAsync(relativePath);
        
        if (entry != null)
        {
            var oldState = entry.State;
            entry.State = FileState.Downloading;
            await _cacheManager.AddOrUpdateEntryAsync(entry);
            OnFileStateChanged(entry, oldState, FileState.Downloading);
        }
        
        _currentProgress.DownloadingCount++;
        _currentProgress.CurrentFile = relativePath;
        _currentProgress.CurrentOperation = "Downloading";
        OnProgressChanged();
        
        try
        {
            var cloudItem = await _cloudProvider.GetItemAsync(relativePath, _cts.Token);
            if (cloudItem == null)
            {
                _logger.LogWarning("Cloud file not found for download: {Path}", relativePath);
                return;
            }
            
            var progress = new Progress<long>(bytes =>
            {
                OnTransferProgress(relativePath, bytes, cloudItem.Size, false);
            });
            
            await _cloudProvider.DownloadFileAsync(relativePath, localPath, progress, _cts.Token);
            
            var fileInfo = new FileInfo(localPath);
            
            var newEntry = new CachedFileEntry
            {
                RelativePath = relativePath,
                LocalPath = localPath,
                FileName = cloudItem.Name,
                IsDirectory = false,
                Size = cloudItem.Size,
                LocalModifiedAt = fileInfo.LastWriteTimeUtc,
                CloudModifiedAt = cloudItem.ModifiedAt,
                CloudETag = cloudItem.ETag,
                LocalContentHash = await _cacheManager.ComputeFileHashAsync(localPath),
                CloudContentHash = cloudItem.ContentHash,
                State = FileState.Synced,
                SyncStatus = SyncStatus.Synced,
                LastAccessedAt = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow,
                IsHydrated = true
            };
            
            await _cacheManager.AddOrUpdateEntryAsync(newEntry);
            OnFileStateChanged(newEntry, FileState.Downloading, FileState.Synced);
            
            _logger.LogInformation("Downloaded file: {Path}", relativePath);
        }
        finally
        {
            _currentProgress.DownloadingCount--;
            OnProgressChanged();
        }
    }
    
    private async Task DeleteRemoteFileAsync(string relativePath)
    {
        var entry = await _cacheManager.GetEntryAsync(relativePath);
        
        try
        {
            await _cloudProvider.DeleteItemAsync(relativePath, recursive: true, _cts.Token);
            await _cacheManager.DeleteEntryAsync(relativePath);
            
            _logger.LogInformation("Deleted remote file: {Path}", relativePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete remote file: {Path}", relativePath);
            throw;
        }
    }
    
    private async Task SyncFromCloudAsync()
    {
        var (cloudItems, _) = await _cloudProvider.GetChangesAsync(
            since: _currentProgress.LastSyncTime,
            cancellationToken: _cts.Token);
            
        var downloadSemaphore = new SemaphoreSlim(_syncSettings.MaxConcurrentDownloads);
        
        foreach (var cloudItem in cloudItems)
        {
            if (cloudItem.IsDirectory) continue;
            
            var entry = await _cacheManager.GetEntryAsync(cloudItem.RelativePath);
            
            if (entry == null)
            {
                // New file in cloud - create placeholder entry
                var newEntry = new CachedFileEntry
                {
                    RelativePath = cloudItem.RelativePath,
                    LocalPath = _cacheManager.GetLocalPath(cloudItem.RelativePath),
                    FileName = cloudItem.Name,
                    IsDirectory = false,
                    Size = cloudItem.Size,
                    CloudModifiedAt = cloudItem.ModifiedAt,
                    CloudETag = cloudItem.ETag,
                    CloudContentHash = cloudItem.ContentHash,
                    State = FileState.CloudOnly,
                    SyncStatus = SyncStatus.Synced,
                    IsHydrated = false
                };
                
                await _cacheManager.AddOrUpdateEntryAsync(newEntry);
                OnFileStateChanged(newEntry, FileState.CloudOnly, FileState.CloudOnly);
            }
            else if (entry.CloudETag != cloudItem.ETag)
            {
                // Cloud file changed
                if (entry.State == FileState.LocallyModified)
                {
                    // Conflict!
                    await HandleConflictAsync(cloudItem.RelativePath, entry, cloudItem);
                }
                else
                {
                    // Download newer version
                    entry.CloudETag = cloudItem.ETag;
                    entry.CloudModifiedAt = cloudItem.ModifiedAt;
                    entry.CloudContentHash = cloudItem.ContentHash;
                    entry.Size = cloudItem.Size;
                    
                    if (entry.IsHydrated)
                    {
                        await downloadSemaphore.WaitAsync(_cts.Token);
                        try
                        {
                            await DownloadFileAsync(cloudItem.RelativePath);
                        }
                        finally
                        {
                            downloadSemaphore.Release();
                        }
                    }
                    else
                    {
                        await _cacheManager.AddOrUpdateEntryAsync(entry);
                    }
                }
            }
        }
    }
    
    private async Task HandleConflictAsync(string relativePath, CachedFileEntry localEntry, CloudFileItem cloudItem)
    {
        var conflict = new SyncConflict
        {
            FilePath = relativePath,
            FileName = localEntry.FileName,
            Type = ConflictType.BothModified,
            LocalModifiedAt = localEntry.LocalModifiedAt,
            LocalSize = localEntry.Size,
            LocalContentHash = localEntry.LocalContentHash,
            CloudModifiedAt = cloudItem.ModifiedAt,
            CloudSize = cloudItem.Size,
            CloudContentHash = cloudItem.ContentHash,
            CloudETag = cloudItem.ETag
        };
        
        _conflicts[relativePath] = conflict;
        _currentProgress.ConflictCount = _conflicts.Count;
        OnProgressChanged();
        
        localEntry.State = FileState.Conflict;
        localEntry.SyncStatus = SyncStatus.Conflict;
        await _cacheManager.AddOrUpdateEntryAsync(localEntry);
        
        OnConflictDetected(conflict);
        OnFileStateChanged(localEntry, FileState.Synced, FileState.Conflict);
        
        _logger.LogWarning("Conflict detected: {Path}", relativePath);
    }
    
    public async Task ResolveConflictAsync(string relativePath, ConflictResolution resolution)
    {
        if (!_conflicts.TryGetValue(relativePath, out var conflict))
        {
            _logger.LogWarning("No conflict found for: {Path}", relativePath);
            return;
        }
        
        var entry = await _cacheManager.GetEntryAsync(relativePath);
        if (entry == null) return;
        
        switch (resolution)
        {
            case ConflictResolution.KeepLocal:
                // Force upload local version
                entry.State = FileState.LocallyModified;
                await _cacheManager.AddOrUpdateEntryAsync(entry);
                await UploadFileAsync(relativePath);
                break;
                
            case ConflictResolution.KeepCloud:
                // Download cloud version
                await DownloadFileAsync(relativePath);
                break;
                
            case ConflictResolution.KeepBoth:
                // Rename local file and download cloud version
                var localPath = _cacheManager.GetLocalPath(relativePath);
                var directory = Path.GetDirectoryName(localPath) ?? "";
                var fileName = Path.GetFileNameWithoutExtension(localPath);
                var ext = Path.GetExtension(localPath);
                var conflictPath = Path.Combine(directory, $"{fileName}_conflict_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
                
                File.Move(localPath, conflictPath);
                
                // Create entry for conflict file
                var conflictRelativePath = _cacheManager.GetRelativePath(conflictPath);
                await ProcessLocalChangeAsync(conflictPath, SyncOperationType.Upload);
                
                // Download cloud version
                await DownloadFileAsync(relativePath);
                break;
        }
        
        conflict.Resolution = resolution;
        conflict.ResolvedAt = DateTime.UtcNow;
        _conflicts.TryRemove(relativePath, out _);
        
        _currentProgress.ConflictCount = _conflicts.Count;
        OnProgressChanged();
        
        _logger.LogInformation("Resolved conflict for {Path} with {Resolution}", relativePath, resolution);
    }
    
    public async Task HydrateFileAsync(string relativePath)
    {
        var entry = await _cacheManager.GetEntryAsync(relativePath);
        if (entry == null || entry.IsHydrated)
            return;
            
        await DownloadFileAsync(relativePath);
    }
    
    public async Task DehydrateFileAsync(string relativePath)
    {
        var entry = await _cacheManager.GetEntryAsync(relativePath);
        if (entry == null || !entry.IsHydrated || entry.IsPinned)
            return;
            
        await _cacheManager.EvictFileAsync(relativePath);
    }
    
    public async Task PinFileAsync(string relativePath, bool pinned)
    {
        await _cacheManager.SetPinnedAsync(relativePath, pinned);
        
        if (pinned)
        {
            await HydrateFileAsync(relativePath);
        }
    }
    
    // Event invocations
    
    private void OnProgressChanged()
    {
        ProgressChanged?.Invoke(this, new SyncProgressEventArgs(_currentProgress));
    }
    
    private void OnFileStateChanged(CachedFileEntry file, FileState oldState, FileState newState)
    {
        var cloudFile = new CloudFileItem
        {
            Id = file.Id,
            Name = file.FileName,
            RelativePath = file.RelativePath,
            IsDirectory = file.IsDirectory,
            Size = file.Size,
            ModifiedAt = file.CloudModifiedAt,
            State = newState
        };
        FileStateChanged?.Invoke(this, new FileStateChangedEventArgs(cloudFile, oldState, newState));
    }
    
    private void OnConflictDetected(SyncConflict conflict)
    {
        ConflictDetected?.Invoke(this, new ConflictDetectedEventArgs(conflict));
    }
    
    private void OnTransferProgress(string filePath, long bytesTransferred, long totalBytes, bool isUpload)
    {
        TransferProgress?.Invoke(this, new TransferProgressEventArgs(filePath, bytesTransferred, totalBytes, isUpload));
    }
    
    private void OnSyncError(string filePath, string message, Exception? ex)
    {
        SyncError?.Invoke(this, new SyncErrorEventArgs(filePath, message, ex));
    }
    
    public void Dispose()
    {
        if (!_isDisposed)
        {
            Stop();
            _cts.Dispose();
            _syncLock.Dispose();
            _isDisposed = true;
        }
    }
}
