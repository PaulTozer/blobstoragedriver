using System.Collections.Concurrent;
using System.Security.AccessControl;
using BlobStorageDriver.CloudProvider;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.Common.Models;
using BlobStorageDriver.SyncEngine.Cache;
using BlobStorageDriver.SyncEngine.Integration;
using DokanNet;
using Microsoft.Extensions.Logging;
using FileAccess = DokanNet.FileAccess;

namespace BlobStorageDriver.SyncEngine.VirtualDrive;

/// <summary>
/// Dokan file system implementation for Azure Blob Storage
/// </summary>
public class BlobFileSystem : IDokanOperations
{
    private readonly ICloudStorageProvider _cloudProvider;
    private readonly LocalCacheManager _cacheManager;
    private readonly CacheSettings _cacheSettings;
    private readonly ILogger<BlobFileSystem> _logger;
    
    // Thread-safe cache of directory listings
    private readonly ConcurrentDictionary<string, (List<CloudFileItem> Items, DateTime FetchedAt)> _directoryCache = new();
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Event raised when file activity occurs (create, modify, delete, upload, download)
    /// </summary>
    public event EventHandler<FileActivityEventArgs>? FileActivity;

    public BlobFileSystem(
        ICloudStorageProvider cloudProvider,
        LocalCacheManager cacheManager,
        CacheSettings cacheSettings,
        ILogger<BlobFileSystem> logger)
    {
        _cloudProvider = cloudProvider;
        _cacheManager = cacheManager;
        _cacheSettings = cacheSettings;
        _logger = logger;
    }

    public NtStatus CreateFile(string fileName, FileAccess access, System.IO.FileShare share, 
        System.IO.FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
    {
        try
        {
            // Handle null/empty filename
            if (string.IsNullOrEmpty(fileName))
            {
                info.IsDirectory = true;
                return NtStatus.Success;
            }
            
            var path = NormalizePath(fileName);
            _logger.LogDebug("CreateFile: {Path}, Mode: {Mode}, Access: {Access}, IsDirectory: {IsDir}", path, mode, access, info.IsDirectory);

            // Handle root directory
            if (string.IsNullOrEmpty(path) || path == "\\" || path == "/")
            {
                info.IsDirectory = true;
                return NtStatus.Success;
            }
            
            var localPath = GetLocalCachePath(path);
            
            // Safely get parent directory
            string? dir = null;
            try
            {
                dir = Path.GetDirectoryName(localPath);
            }
            catch
            {
                // Invalid path
                return NtStatus.ObjectNameInvalid;
            }
            
            // Check if it's a directory request
            if (info.IsDirectory)
            {
                return HandleDirectoryRequest(path, localPath, dir, mode);
            }

            // Ensure parent directory exists for file operations
            try
            {
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create directory {Dir}", dir);
            }

            // Handle file operations
            return HandleFileRequest(path, localPath, mode, access, info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateFile for {fileName}", fileName);
            return NtStatus.InternalError;
        }
    }
    
    private NtStatus HandleDirectoryRequest(string path, string localPath, string? dir, System.IO.FileMode mode)
    {
        try
        {
            if (mode == System.IO.FileMode.CreateNew || mode == System.IO.FileMode.Create)
            {
                // Creating a new directory
                if (!Directory.Exists(localPath))
                {
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    Directory.CreateDirectory(localPath);
                }
                return NtStatus.Success;
            }
            
            // Opening existing directory
            if (Directory.Exists(localPath))
                return NtStatus.Success;
                
            // Check cloud
            var items = GetDirectoryItems(path);
            return items != null ? NtStatus.Success : NtStatus.ObjectPathNotFound;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in HandleDirectoryRequest for {path}", path);
            return NtStatus.InternalError;
        }
    }
    
    private NtStatus HandleFileRequest(string path, string localPath, System.IO.FileMode mode, FileAccess access, IDokanFileInfo info)
    {
        try
        {
            switch (mode)
            {
                case System.IO.FileMode.CreateNew:
                    if (File.Exists(localPath))
                        return NtStatus.ObjectNameCollision;
                    // Fall through to Create
                    goto case System.IO.FileMode.Create;

                case System.IO.FileMode.Create:
                case System.IO.FileMode.Truncate:
                    // Create or truncate file
                    try
                    {
                        using (File.Create(localPath)) { }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create file {localPath}", localPath);
                        return NtStatus.ObjectNameInvalid;
                    }
                    info.Context = localPath;
                    return NtStatus.Success;

                case System.IO.FileMode.OpenOrCreate:
                    if (!File.Exists(localPath))
                    {
                        try
                        {
                            using (File.Create(localPath)) { }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to create file {localPath}", localPath);
                            return NtStatus.ObjectNameInvalid;
                        }
                    }
                    info.Context = localPath;
                    return NtStatus.Success;

                case System.IO.FileMode.Open:
                    // Check if file exists locally
                    if (File.Exists(localPath))
                    {
                        info.Context = localPath;
                        return NtStatus.Success;
                    }
                    
                    // Try to download from cloud
                    return TryDownloadFromCloud(path, localPath, info);

                default:
                    // For any other mode, just set the context
                    info.Context = localPath;
                    return NtStatus.Success;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in HandleFileRequest for {path}", path);
            return NtStatus.InternalError;
        }
    }
    
    private NtStatus TryDownloadFromCloud(string path, string localPath, IDokanFileInfo info)
    {
        try
        {
            var cloudFileInfo = GetFileInfo(path);
            if (cloudFileInfo == null)
                return NtStatus.ObjectNameNotFound;
                
            _cloudProvider.DownloadFileAsync(path, localPath)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            info.Context = localPath;
            return NtStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download file {path}", path);
            return NtStatus.ObjectNameNotFound;
        }
    }

    public void Cleanup(string fileName, IDokanFileInfo info)
    {
        try
        {
            var path = NormalizePath(fileName);
            _logger.LogDebug("Cleanup: {Path}, DeletePending: {Delete}", path, info.DeletePending);
            
            // Clear context
            info.Context = null;
            
            if (info.DeletePending)
            {
                try
                {
                    var localPath = GetLocalCachePath(path);
                    
                    FileActivity?.Invoke(this, new FileActivityEventArgs(
                        FileActivityType.Deleted, path, info.IsDirectory, "Deleting..."));
                    
                    if (info.IsDirectory)
                    {
                        if (Directory.Exists(localPath))
                            Directory.Delete(localPath, true);
                        
                        // Delete directory marker in blob storage (fire and forget)
                        _ = Task.Run(async () => {
                            try { await _cloudProvider.DeleteItemAsync(path.TrimEnd('/') + "/"); } catch { }
                        });
                    }
                    else
                    {
                        // Delete file
                        if (File.Exists(localPath))
                            File.Delete(localPath);
                        
                        // Delete from cloud (fire and forget)
                        _ = Task.Run(async () => {
                            try 
                            { 
                                await _cloudProvider.DeleteItemAsync(path);
                                await _cacheManager.DeleteEntryAsync(path);
                            } 
                            catch { }
                        });
                    }
                    
                    InvalidateCache(Path.GetDirectoryName(path) ?? "");
                    
                    FileActivity?.Invoke(this, new FileActivityEventArgs(
                        FileActivityType.Deleted, path, info.IsDirectory, "Deleted"));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting {Path}", path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Cleanup for {fileName}", fileName);
        }
    }

    public void CloseFile(string fileName, IDokanFileInfo info)
    {
        try
        {
            var path = NormalizePath(fileName);
            _logger.LogDebug("CloseFile: {Path}, IsDir: {IsDir}", path, info.IsDirectory);
            
            // Clear context
            info.Context = null;
            
            // Skip directories
            if (info.IsDirectory || info.DeletePending)
                return;
                
            var localPath = GetLocalCachePath(path);
            if (!File.Exists(localPath))
                return;
                
            System.IO.FileInfo fileInfo;
            try
            {
                fileInfo = new System.IO.FileInfo(localPath);
            }
            catch
            {
                return; // File may have been deleted
            }
            
            // Only upload if file has content
            if (fileInfo.Length > 0)
            {
                var filePath = path; // Capture for closure
                var fileSize = fileInfo.Length;
                var fileName2 = fileInfo.Name;
                
                FileActivity?.Invoke(this, new FileActivityEventArgs(
                    FileActivityType.Uploading, filePath, false, $"Uploading ({FormatSize(fileSize)})..."));
                
                // Fire and forget upload - don't block Dokan callback
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _cloudProvider.UploadFileAsync(localPath, filePath);
                        
                        // Update cache entry
                        var entry = new CachedFileEntry
                        {
                            RelativePath = filePath,
                            FileName = fileName2,
                            LocalPath = localPath,
                            Size = fileSize,
                            CloudETag = "", // Will be updated on next sync
                            LocalModifiedAt = DateTime.UtcNow,
                            CloudModifiedAt = DateTime.UtcNow,
                            State = FileState.Synced,
                            IsHydrated = true
                        };
                        await _cacheManager.AddOrUpdateEntryAsync(entry);
                        
                        InvalidateCache(Path.GetDirectoryName(filePath) ?? "");
                        
                        FileActivity?.Invoke(this, new FileActivityEventArgs(
                            FileActivityType.Uploaded, filePath, false, $"Uploaded to Azure ({FormatSize(fileSize)})"));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to upload {Path} to cloud", filePath);
                        FileActivity?.Invoke(this, new FileActivityEventArgs(
                            FileActivityType.Error, filePath, false, $"Upload failed: {ex.Message}"));
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CloseFile for {fileName}", fileName);
        }
    }
    
    private static string FormatSize(long bytes)
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

    public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
    {
        var path = NormalizePath(fileName);
        bytesRead = 0;

        try
        {
            // Get local path from context or compute it
            var localPath = info.Context as string ?? GetLocalCachePath(path);
            
            // Ensure file is cached locally
            if (!File.Exists(localPath))
            {
                // Download from cloud
                var dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                
                FileActivity?.Invoke(this, new FileActivityEventArgs(
                    FileActivityType.Downloading, path, false, "Downloading from Azure..."));
                
                try
                {
                    _cloudProvider.DownloadFileAsync(path, localPath)
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                }
                catch (AggregateException ae)
                {
                    _logger.LogError(ae.InnerException ?? ae, "Failed to download {Path}", path);
                    return NtStatus.ObjectNameNotFound;
                }
                
                if (!File.Exists(localPath))
                    return NtStatus.ObjectNameNotFound;
                
                var fileSize = new System.IO.FileInfo(localPath).Length;
                FileActivity?.Invoke(this, new FileActivityEventArgs(
                    FileActivityType.Downloaded, path, false, $"Downloaded ({FormatSize(fileSize)})"));
            }

            // Retry loop for file access
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    using var fs = new FileStream(localPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                    fs.Seek(offset, SeekOrigin.Begin);
                    bytesRead = fs.Read(buffer, 0, buffer.Length);
                    return NtStatus.Success;
                }
                catch (IOException) when (retry < 2)
                {
                    Thread.Sleep(10); // Brief wait before retry
                }
            }
            
            return NtStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file {Path}", path);
            return NtStatus.InternalError;
        }
    }

    public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
    {
        var path = NormalizePath(fileName);
        bytesWritten = 0;

        try
        {
            // Get local path from context or compute it
            var localPath = info.Context as string ?? GetLocalCachePath(path);
            
            // Ensure directory exists
            var dir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Retry loop for file access
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    using var fs = new FileStream(localPath, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                    fs.Seek(offset, SeekOrigin.Begin);
                    fs.Write(buffer, 0, buffer.Length);
                    bytesWritten = buffer.Length;
                    return NtStatus.Success;
                }
                catch (IOException) when (retry < 2)
                {
                    Thread.Sleep(10); // Brief wait before retry
                }
            }
            
            return NtStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing file {Path}", path);
            return NtStatus.InternalError;
        }
    }

    public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info)
    {
        return NtStatus.Success;
    }

    public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
    {
        var path = NormalizePath(fileName);
        fileInfo = new FileInformation();

        try
        {
            // Root directory
            if (string.IsNullOrEmpty(path) || path == "\\")
            {
                fileInfo = new FileInformation
                {
                    FileName = "\\",
                    Attributes = FileAttributes.Directory,
                    CreationTime = DateTime.Now,
                    LastAccessTime = DateTime.Now,
                    LastWriteTime = DateTime.Now
                };
                return NtStatus.Success;
            }

            // Check local cache first
            var localPath = GetLocalCachePath(path);
            if (File.Exists(localPath))
            {
                var fi = new System.IO.FileInfo(localPath);
                fileInfo = new FileInformation
                {
                    FileName = fi.Name,
                    Attributes = fi.Attributes,
                    CreationTime = fi.CreationTime,
                    LastAccessTime = fi.LastAccessTime,
                    LastWriteTime = fi.LastWriteTime,
                    Length = fi.Length
                };
                return NtStatus.Success;
            }
            
            if (Directory.Exists(localPath))
            {
                var di = new DirectoryInfo(localPath);
                fileInfo = new FileInformation
                {
                    FileName = di.Name,
                    Attributes = di.Attributes,
                    CreationTime = di.CreationTime,
                    LastAccessTime = di.LastAccessTime,
                    LastWriteTime = di.LastWriteTime
                };
                return NtStatus.Success;
            }

            // Check cloud
            var cloudItem = GetFileInfo(path);
            if (cloudItem != null)
            {
                fileInfo = new FileInformation
                {
                    FileName = cloudItem.Name,
                    Attributes = cloudItem.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
                    CreationTime = cloudItem.CreatedAt,
                    LastAccessTime = cloudItem.ModifiedAt,
                    LastWriteTime = cloudItem.ModifiedAt,
                    Length = cloudItem.Size
                };
                return NtStatus.Success;
            }

            return NtStatus.ObjectNameNotFound;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file info for {Path}", path);
            return NtStatus.InternalError;
        }
    }

    public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info)
    {
        return FindFilesWithPattern(fileName, "*", out files, info);
    }

    public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info)
    {
        var path = NormalizePath(fileName);
        files = new List<FileInformation>();

        try
        {
            var items = GetDirectoryItems(path);
            if (items == null)
                return NtStatus.ObjectPathNotFound;

            foreach (var item in items)
            {
                // Apply search pattern filter
                if (searchPattern != "*" && !MatchesPattern(item.Name, searchPattern))
                    continue;

                files.Add(new FileInformation
                {
                    FileName = item.Name,
                    Attributes = item.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
                    CreationTime = item.CreatedAt,
                    LastAccessTime = item.ModifiedAt,
                    LastWriteTime = item.ModifiedAt,
                    Length = item.Size
                });
            }

            return NtStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing directory {Path}", path);
            return NtStatus.InternalError;
        }
    }

    public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info) => NtStatus.Success;
    public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, IDokanFileInfo info) => NtStatus.Success;
    public NtStatus DeleteFile(string fileName, IDokanFileInfo info) => NtStatus.Success;
    public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info) => NtStatus.Success;
    public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
    {
        try
        {
            var oldPath = NormalizePath(oldName);
            var newPath = NormalizePath(newName);
            
            var oldLocalPath = GetLocalCachePath(oldPath);
            var newLocalPath = GetLocalCachePath(newPath);
            
            if (File.Exists(oldLocalPath))
            {
                var dir = Path.GetDirectoryName(newLocalPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                    
                File.Move(oldLocalPath, newLocalPath, replace);
            }
            
            // Update in cloud (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _cloudProvider.MoveItemAsync(oldPath, newPath);
                    InvalidateCache(Path.GetDirectoryName(oldPath) ?? "");
                    InvalidateCache(Path.GetDirectoryName(newPath) ?? "");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to move {OldPath} to {NewPath} in cloud", oldPath, newPath);
                }
            });
            
            return NtStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving {oldName} to {newName}", oldName, newName);
            return NtStatus.InternalError;
        }
    }
    
    public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info)
    {
        try
        {
            var path = NormalizePath(fileName);
            
            // Get local path from context or compute it
            var localPath = info.Context as string ?? GetLocalCachePath(path);
            
            // Ensure directory exists
            var dir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
            // Retry loop for file access
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    using var fs = new FileStream(localPath, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                    fs.SetLength(length);
                    return NtStatus.Success;
                }
                catch (IOException) when (retry < 2)
                {
                    Thread.Sleep(10);
                }
            }
            return NtStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetEndOfFile failed for {fileName}", fileName);
            return NtStatus.Success; // Return success to avoid crashes
        }
    }
    
    public NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info) => SetEndOfFile(fileName, length, info);

    public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info) => NtStatus.Success;
    public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info) => NtStatus.Success;

    public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, IDokanFileInfo info)
    {
        // Report a large virtual disk
        totalNumberOfBytes = 1L * 1024 * 1024 * 1024 * 1024; // 1 TB
        freeBytesAvailable = totalNumberOfBytes - (_cacheSettings.MaxCacheSizeBytes / 2);
        totalNumberOfFreeBytes = freeBytesAvailable;
        return NtStatus.Success;
    }

    public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, 
        out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
    {
        volumeLabel = "Azure Blob";
        fileSystemName = "NTFS";
        maximumComponentLength = 256;
        features = FileSystemFeatures.CasePreservedNames | 
                   FileSystemFeatures.CaseSensitiveSearch |
                   FileSystemFeatures.UnicodeOnDisk;
        return NtStatus.Success;
    }

    public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity? security, AccessControlSections sections, IDokanFileInfo info)
    {
        security = null;
        return NtStatus.NotImplemented;
    }

    public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
    {
        return NtStatus.NotImplemented;
    }

    public NtStatus Mounted(string mountPoint, IDokanFileInfo info)
    {
        _logger.LogInformation("Blob Storage mounted at {MountPoint}", mountPoint);
        return NtStatus.Success;
    }

    public NtStatus Unmounted(IDokanFileInfo info)
    {
        _logger.LogInformation("Blob Storage unmounted");
        return NtStatus.Success;
    }

    public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info)
    {
        streams = Array.Empty<FileInformation>();
        return NtStatus.NotImplemented;
    }

    #region Helper Methods

    private string NormalizePath(string path)
    {
        // Convert Windows path to blob path
        path = path.Replace("\\", "/").TrimStart('/');
        return path;
    }

    private string GetLocalCachePath(string blobPath)
    {
        return Path.Combine(_cacheSettings.LocalSyncFolder, blobPath.Replace("/", "\\"));
    }

    private List<CloudFileItem>? GetDirectoryItems(string path)
    {
        // Check cache first (thread-safe)
        if (_directoryCache.TryGetValue(path, out var cached) && 
            DateTime.UtcNow - cached.FetchedAt < _cacheLifetime)
        {
            return cached.Items;
        }

        // Fetch from cloud (outside of any lock)
        try
        {
            var items = _cloudProvider.ListItemsAsync(path)
                .ConfigureAwait(false).GetAwaiter().GetResult().ToList();
            _directoryCache[path] = (items, DateTime.UtcNow);
            return items;
        }
        catch (AggregateException ae)
        {
            _logger.LogError(ae.InnerException ?? ae, "Error listing directory {Path}", path);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing directory {Path}", path);
            return null;
        }
    }

    private CloudFileItem? GetFileInfo(string path)
    {
        var dirPath = Path.GetDirectoryName(path)?.Replace("\\", "/") ?? "";
        var fileName = Path.GetFileName(path);
        
        var items = GetDirectoryItems(dirPath);
        return items?.FirstOrDefault(i => i.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    private void InvalidateCache(string path)
    {
        var normalizedPath = path.Replace("\\", "/");
        _directoryCache.TryRemove(normalizedPath, out _);
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (pattern == "*" || pattern == "*.*")
            return true;
            
        // Simple wildcard matching
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(fileName, regex, 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    #endregion
}
