using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.Common.Models;
using LiteDB;
using Microsoft.Extensions.Logging;

namespace BlobStorageDriver.SyncEngine.Cache;

/// <summary>
/// Manages local file cache with LRU eviction and metadata tracking
/// </summary>
public class LocalCacheManager : IDisposable
{
    private readonly CacheSettings _settings;
    private readonly ILogger<LocalCacheManager> _logger;
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<CachedFileEntry> _filesCollection;
    private readonly ILiteCollection<PendingSyncOperation> _pendingOpsCollection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public LocalCacheManager(CacheSettings settings, ILogger<LocalCacheManager> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        var dbPath = settings.DatabasePath;
        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }
        
        _database = new LiteDatabase(dbPath);
        _filesCollection = _database.GetCollection<CachedFileEntry>("files");
        _pendingOpsCollection = _database.GetCollection<PendingSyncOperation>("pending_operations");
        
        // Create indexes
        _filesCollection.EnsureIndex(x => x.RelativePath, unique: true);
        _filesCollection.EnsureIndex(x => x.State);
        _filesCollection.EnsureIndex(x => x.LastAccessedAt);
        _filesCollection.EnsureIndex(x => x.IsPinned);
        
        _pendingOpsCollection.EnsureIndex(x => x.RelativePath);
        _pendingOpsCollection.EnsureIndex(x => x.QueuedAt);
        _pendingOpsCollection.EnsureIndex(x => x.Priority);
        
        EnsureSyncFolderExists();
        
        _logger.LogInformation("Local cache manager initialized. Database: {DbPath}", dbPath);
    }
    
    private void EnsureSyncFolderExists()
    {
        if (!Directory.Exists(_settings.LocalSyncFolder))
        {
            Directory.CreateDirectory(_settings.LocalSyncFolder);
            _logger.LogInformation("Created sync folder: {Folder}", _settings.LocalSyncFolder);
        }
    }
    
    public string GetLocalPath(string relativePath)
    {
        return Path.Combine(_settings.LocalSyncFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
    
    public string GetRelativePath(string localPath)
    {
        var fullSyncPath = Path.GetFullPath(_settings.LocalSyncFolder);
        var fullLocalPath = Path.GetFullPath(localPath);
        
        if (!fullLocalPath.StartsWith(fullSyncPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Path {localPath} is not within sync folder {_settings.LocalSyncFolder}");
        }
        
        var relativePath = fullLocalPath.Substring(fullSyncPath.Length)
            .TrimStart(Path.DirectorySeparatorChar)
            .Replace(Path.DirectorySeparatorChar, '/');
            
        return relativePath;
    }
    
    public async Task<CachedFileEntry?> GetEntryAsync(string relativePath)
    {
        await _lock.WaitAsync();
        try
        {
            return _filesCollection.FindOne(x => x.RelativePath == relativePath);
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<CachedFileEntry> AddOrUpdateEntryAsync(CachedFileEntry entry)
    {
        await _lock.WaitAsync();
        try
        {
            var existing = _filesCollection.FindOne(x => x.RelativePath == entry.RelativePath);
            if (existing != null)
            {
                entry.Id = existing.Id;
                _filesCollection.Update(entry);
            }
            else
            {
                entry.Id = Guid.NewGuid().ToString();
                _filesCollection.Insert(entry);
            }
            return entry;
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<bool> DeleteEntryAsync(string relativePath)
    {
        await _lock.WaitAsync();
        try
        {
            var deleted = _filesCollection.DeleteMany(x => x.RelativePath == relativePath);
            return deleted > 0;
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<IEnumerable<CachedFileEntry>> GetAllEntriesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return _filesCollection.FindAll().ToList();
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<IEnumerable<CachedFileEntry>> GetEntriesByStateAsync(FileState state)
    {
        await _lock.WaitAsync();
        try
        {
            return _filesCollection.Find(x => x.State == state).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<IEnumerable<CachedFileEntry>> GetEntriesInFolderAsync(string folderPath)
    {
        await _lock.WaitAsync();
        try
        {
            var prefix = string.IsNullOrEmpty(folderPath) ? "" : folderPath.TrimEnd('/') + "/";
            return _filesCollection
                .Find(x => x.RelativePath.StartsWith(prefix))
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task UpdateAccessTimeAsync(string relativePath)
    {
        await _lock.WaitAsync();
        try
        {
            var entry = _filesCollection.FindOne(x => x.RelativePath == relativePath);
            if (entry != null)
            {
                entry.LastAccessedAt = DateTime.UtcNow;
                _filesCollection.Update(entry);
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task SetPinnedAsync(string relativePath, bool pinned)
    {
        await _lock.WaitAsync();
        try
        {
            var entry = _filesCollection.FindOne(x => x.RelativePath == relativePath);
            if (entry != null)
            {
                entry.IsPinned = pinned;
                _filesCollection.Update(entry);
                _logger.LogInformation("Set pinned status for {Path}: {Pinned}", relativePath, pinned);
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<long> GetCurrentCacheSizeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return _filesCollection
                .Find(x => x.IsHydrated && !x.IsDirectory)
                .Sum(x => x.Size);
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<IEnumerable<CachedFileEntry>> GetFilesForEvictionAsync(long bytesToFree)
    {
        await _lock.WaitAsync();
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-_settings.KeepAccessedWithinDays);
            
            // Get non-pinned, hydrated files ordered by last access time
            var candidates = _filesCollection
                .Find(x => x.IsHydrated && 
                          !x.IsPinned && 
                          !x.IsDirectory &&
                          x.State == FileState.Synced &&
                          x.LastAccessedAt < cutoffDate)
                .OrderBy(x => x.LastAccessedAt)
                .ToList();
                
            var filesToEvict = new List<CachedFileEntry>();
            long freedBytes = 0;
            
            foreach (var file in candidates)
            {
                if (freedBytes >= bytesToFree)
                    break;
                    
                filesToEvict.Add(file);
                freedBytes += file.Size;
            }
            
            return filesToEvict;
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task EvictFileAsync(string relativePath)
    {
        await _lock.WaitAsync();
        try
        {
            var entry = _filesCollection.FindOne(x => x.RelativePath == relativePath);
            if (entry != null && entry.IsHydrated && !entry.IsPinned)
            {
                var localPath = GetLocalPath(relativePath);
                if (File.Exists(localPath))
                {
                    // Create placeholder instead of deleting
                    var placeholder = new FileInfo(localPath);
                    File.Delete(localPath);
                    
                    // Mark as cloud-only
                    entry.IsHydrated = false;
                    entry.State = FileState.CloudOnly;
                    _filesCollection.Update(entry);
                    
                    _logger.LogInformation("Evicted file from cache: {Path}", relativePath);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<bool> EnforceSpaceLimitAsync()
    {
        var currentSize = await GetCurrentCacheSizeAsync();
        var thresholdSize = (long)(_settings.MaxCacheSizeBytes * _settings.EvictionThresholdPercent / 100);
        
        if (currentSize <= thresholdSize)
            return true;
            
        var bytesToFree = currentSize - (long)(_settings.MaxCacheSizeBytes * 0.7); // Free to 70%
        var filesToEvict = await GetFilesForEvictionAsync(bytesToFree);
        
        foreach (var file in filesToEvict)
        {
            await EvictFileAsync(file.RelativePath);
        }
        
        _logger.LogInformation("Cache eviction complete. Evicted {Count} files, freed approximately {Bytes} bytes", 
            filesToEvict.Count(), filesToEvict.Sum(f => f.Size));
            
        return true;
    }
    
    // Pending operations management
    
    public async Task<PendingSyncOperation> QueueOperationAsync(PendingSyncOperation operation)
    {
        await _lock.WaitAsync();
        try
        {
            operation.Id = Guid.NewGuid().ToString();
            _pendingOpsCollection.Insert(operation);
            _logger.LogDebug("Queued operation: {Type} for {Path}", operation.OperationType, operation.RelativePath);
            return operation;
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<IEnumerable<PendingSyncOperation>> GetPendingOperationsAsync(int limit = 100)
    {
        await _lock.WaitAsync();
        try
        {
            return _pendingOpsCollection
                .Find(x => true)
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.QueuedAt)
                .Take(limit)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<bool> CompleteOperationAsync(string operationId)
    {
        await _lock.WaitAsync();
        try
        {
            return _pendingOpsCollection.Delete(operationId);
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task UpdateOperationAsync(PendingSyncOperation operation)
    {
        await _lock.WaitAsync();
        try
        {
            _pendingOpsCollection.Update(operation);
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<int> GetPendingOperationCountAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return _pendingOpsCollection.Count();
        }
        finally
        {
            _lock.Release();
        }
    }
    
    // Hash computation
    
    public async Task<string> ComputeFileHashAsync(string localPath)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        await using var stream = File.OpenRead(localPath);
        var hash = await md5.ComputeHashAsync(stream);
        return Convert.ToBase64String(hash);
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _lock.Dispose();
            _database.Dispose();
            _disposed = true;
        }
    }
}
