using BlobStorageDriver.CloudProvider;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.SyncEngine.Cache;
using BlobStorageDriver.SyncEngine.Integration;
using DokanNet;
using DokanNet.Logging;
using Microsoft.Extensions.Logging;

namespace BlobStorageDriver.SyncEngine.VirtualDrive;

/// <summary>
/// Manages the virtual drive mount/unmount lifecycle
/// </summary>
public class VirtualDriveManager : IDisposable
{
    private readonly ICloudStorageProvider _cloudProvider;
    private readonly LocalCacheManager _cacheManager;
    private readonly AppConfiguration _config;
    private readonly ILogger<VirtualDriveManager> _logger;
    private readonly ILogger<BlobFileSystem> _fileSystemLogger;
    
    private IDokanOperations? _fileSystem;
    private Dokan? _dokan;
    private DokanInstance? _dokanInstance;
    private Task? _mountTask;
    private CancellationTokenSource? _mountCts;
    private FileSystemWatcher? _fileWatcher;
    private bool _isMounted;
    private readonly object _mountLock = new();

    public bool IsMounted => _isMounted;
    public string? MountPoint { get; private set; }
    
    /// <summary>
    /// Event raised when file activity occurs in the virtual drive
    /// </summary>
    public event EventHandler<FileActivityEventArgs>? FileActivity;

    public VirtualDriveManager(
        ICloudStorageProvider cloudProvider,
        LocalCacheManager cacheManager,
        AppConfiguration config,
        ILogger<VirtualDriveManager> logger,
        ILogger<BlobFileSystem> fileSystemLogger)
    {
        _cloudProvider = cloudProvider;
        _cacheManager = cacheManager;
        _config = config;
        _logger = logger;
        _fileSystemLogger = fileSystemLogger;
    }

    /// <summary>
    /// Mount the Azure Blob Storage as a virtual drive
    /// </summary>
    public async Task<bool> MountAsync(CancellationToken cancellationToken = default)
    {
        lock (_mountLock)
        {
            if (_isMounted)
            {
                _logger.LogWarning("Virtual drive is already mounted at {MountPoint}", MountPoint);
                return true;
            }
        }

        var driveLetter = _config.Integration.DriveLetter;
        var volumeLabel = _config.Integration.VolumeLabel;

        if (string.IsNullOrEmpty(driveLetter))
        {
            driveLetter = "Z";
        }

        var mountPoint = $"{driveLetter}:\\";

        _logger.LogInformation("Mounting Azure Blob Storage as virtual drive {MountPoint}", mountPoint);

        try
        {
            // Use simple mirror file system for stability
            var mirrorRoot = _config.Cache.LocalSyncFolder;
            _logger.LogInformation("Using mirror root: {MirrorRoot}", mirrorRoot);
            
            _fileSystem = new MirrorFileSystem(mirrorRoot, _fileSystemLogger);
            
            // Create Dokan instance
            _dokan = new Dokan(new DokanNet.Logging.NullLogger());
            
            var dokanBuilder = new DokanInstanceBuilder(_dokan)
                .ConfigureOptions(options =>
                {
                    options.Options = DokanOptions.FixedDrive;
                    options.MountPoint = mountPoint;
                    options.SingleThread = true;
                });

            _dokanInstance = dokanBuilder.Build(_fileSystem);
            _logger.LogInformation("DokanInstance created for {MountPoint}", mountPoint);

            // Store the mount point
            MountPoint = mountPoint;
            
            // Create cancellation token for the wait task
            _mountCts = new CancellationTokenSource();
            
            // Start a background task that waits for the file system to close
            // This keeps the mount alive
            _mountTask = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("Waiting for file system to close...");
                    await _dokanInstance.WaitForFileSystemClosedAsync(uint.MaxValue);
                    _logger.LogInformation("File system closed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error waiting for file system close");
                }
                finally
                {
                    lock (_mountLock)
                    {
                        _isMounted = false;
                    }
                }
            }, _mountCts.Token);

            // Give it a moment to mount
            await Task.Delay(500, cancellationToken);

            lock (_mountLock)
            {
                _isMounted = true;
            }

            // Start file watcher to track activity
            StartFileWatcher(mirrorRoot);

            _logger.LogInformation("Successfully mounted Azure Blob Storage at {MountPoint}", mountPoint);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mount virtual drive at {MountPoint}", mountPoint);
            return false;
        }
    }

    /// <summary>
    /// Unmount the virtual drive
    /// </summary>
    public async Task UnmountAsync()
    {
        lock (_mountLock)
        {
            if (!_isMounted)
            {
                _logger.LogWarning("Virtual drive is not currently mounted");
                return;
            }
        }

        _logger.LogInformation("Unmounting virtual drive from {MountPoint}", MountPoint);

        try
        {
            // Cancel the mount wait task
            _mountCts?.Cancel();
            
            // Dispose the dokan instance to trigger unmount
            _dokanInstance?.Dispose();
            _dokanInstance = null;
            
            // Wait for the mount task to complete
            if (_mountTask != null)
            {
                try
                {
                    await _mountTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Mount task did not complete in time");
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
                _mountTask = null;
            }
            
            _dokan?.Dispose();
            _dokan = null;
            _mountCts?.Dispose();
            _mountCts = null;
            _fileSystem = null;

            lock (_mountLock)
            {
                _isMounted = false;
                MountPoint = null;
            }

            _logger.LogInformation("Successfully unmounted virtual drive");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unmounting virtual drive");
        }
    }

    /// <summary>
    /// Check if Dokan driver is installed
    /// </summary>
    public static bool IsDokanInstalled()
    {
        try
        {
            var dokan = new Dokan(new DokanNet.Logging.NullLogger());
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get available drive letters (excludes A and B which are reserved for floppy drives)
    /// </summary>
    public static IEnumerable<string> GetAvailableDriveLetters()
    {
        var usedDrives = DriveInfo.GetDrives().Select(d => d.Name[0]).ToHashSet();
        // Exclude A and B (reserved for floppy drives)
        usedDrives.Add('A');
        usedDrives.Add('B');
        
        for (char c = 'C'; c <= 'Z'; c++)
        {
            if (!usedDrives.Contains(c))
                yield return c.ToString();
        }
    }

    public void Dispose()
    {
        if (_isMounted)
        {
            UnmountAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        
        StopFileWatcher();
        _mountCts?.Dispose();
        _dokanInstance?.Dispose();
        _dokan?.Dispose();
    }
    
    #region File Activity Tracking
    
    private void StartFileWatcher(string watchPath)
    {
        if (!Directory.Exists(watchPath))
            return;
            
        _fileWatcher = new FileSystemWatcher(watchPath)
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
        
        _logger.LogInformation("File activity watcher started for: {Path}", watchPath);
    }
    
    private void StopFileWatcher()
    {
        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Created -= OnFileCreated;
            _fileWatcher.Changed -= OnFileChanged;
            _fileWatcher.Deleted -= OnFileDeleted;
            _fileWatcher.Renamed -= OnFileRenamed;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }
    }
    
    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (ShouldSkipFile(e.FullPath)) return;
        
        var isDirectory = Directory.Exists(e.FullPath);
        var relativePath = GetRelativePath(e.FullPath);
        
        _logger.LogDebug("{Type} created: {Path}", isDirectory ? "Folder" : "File", relativePath);
        
        FileActivity?.Invoke(this, new FileActivityEventArgs(
            FileActivityType.Created,
            relativePath,
            isDirectory,
            $"{(isDirectory ? "Folder" : "File")} created"));
    }
    
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (ShouldSkipFile(e.FullPath)) return;
        if (Directory.Exists(e.FullPath)) return; // Skip directory change events
        
        var relativePath = GetRelativePath(e.FullPath);
        
        _logger.LogDebug("File modified: {Path}", relativePath);
        
        FileActivity?.Invoke(this, new FileActivityEventArgs(
            FileActivityType.Modified,
            relativePath,
            false,
            "File modified"));
    }
    
    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (ShouldSkipFile(e.FullPath)) return;
        
        var relativePath = GetRelativePath(e.FullPath);
        
        _logger.LogDebug("Deleted: {Path}", relativePath);
        
        FileActivity?.Invoke(this, new FileActivityEventArgs(
            FileActivityType.Deleted,
            relativePath,
            false, // Can't tell if it was a directory after deletion
            "Deleted"));
    }
    
    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (ShouldSkipFile(e.FullPath)) return;
        
        var isDirectory = Directory.Exists(e.FullPath);
        var oldRelativePath = GetRelativePath(e.OldFullPath);
        var newRelativePath = GetRelativePath(e.FullPath);
        
        _logger.LogDebug("Renamed: {OldPath} -> {NewPath}", oldRelativePath, newRelativePath);
        
        FileActivity?.Invoke(this, new FileActivityEventArgs(
            FileActivityType.Renamed,
            newRelativePath,
            isDirectory,
            $"Renamed from {oldRelativePath}"));
    }
    
    private string GetRelativePath(string fullPath)
    {
        var syncFolder = _config.Cache.LocalSyncFolder;
        if (fullPath.StartsWith(syncFolder, StringComparison.OrdinalIgnoreCase))
        {
            var relative = fullPath.Substring(syncFolder.Length);
            return relative.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        return fullPath;
    }
    
    private static bool ShouldSkipFile(string path)
    {
        var fileName = Path.GetFileName(path);
        
        // Skip system and temporary files
        return fileName.StartsWith("~$") ||
               fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("System Volume Information");
    }
    
    #endregion
}
